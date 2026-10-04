using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class OpenEndedRollingHorizonTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Open_ended_visit_rolls_across_two_planning_horizons_without_terminal_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"rolling-{suffix}", $"ROLLING-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RH-{suffix[..2]}-{suffix[2..4]}", $"RH{suffix[..4]}", null);
        var product = new ParkingProviderProduct(
            Guid.NewGuid(),
            $"rolling-{suffix}",
            "Rolling horizon test product",
            null,
            null,
            "TEST",
            now);
        var ruleSet = new ParkingRuleSet(
            Guid.NewGuid(),
            now.AddDays(-1),
            now.AddDays(30),
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());
        ruleSet.AssignProviderProduct(product.Id);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddHours(-1),
            null,
            new EffectiveParkingPolicySnapshot(null, null, true),
            product.Id,
            product.ProviderProductId,
            product.Location);
        visit.Activate();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now.AddMinutes(-1));
        work.Claim("rolling-horizon-1", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.ParkingProviderProducts.Add(product);
            seedContext.ParkingRuleSets.Add(ruleSet);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            services.AddSingleton<TimeProvider>(clock);
            services.AddLogging();
            await using var provider = services.BuildServiceProvider();

            await ProcessAsync(provider, work.Id, cancellationToken);

            DateTimeOffset firstHorizon;
            await using (var firstVerify = fixture.CreateDbContext())
            {
                var persistedVisit = await firstVerify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
                var persistedWork = await firstVerify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
                firstHorizon = now.AddDays(14);

                Assert.Equal(VisitStatus.Active, persistedVisit.Status);
                Assert.Null(persistedVisit.DesiredEndAt);
                Assert.Equal(VisitSchedulerWorkStatus.Pending, persistedWork.Status);
                Assert.Equal(firstHorizon, persistedWork.DueAt);
                Assert.False(await firstVerify.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.StopVisit,
                    cancellationToken));
            }

            clock.SetUtcNow(firstHorizon);
            await ClaimAsync(work.Id, "rolling-horizon-2", firstHorizon, cancellationToken);
            await ProcessAsync(provider, work.Id, cancellationToken);

            await using var secondVerify = fixture.CreateDbContext();
            var secondVisit = await secondVerify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
            var secondWork = await secondVerify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);

            Assert.Equal(VisitStatus.Active, secondVisit.Status);
            Assert.Null(secondVisit.DesiredEndAt);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, secondWork.Status);
            Assert.Equal(firstHorizon.AddDays(14), secondWork.DueAt);
            Assert.False(await secondVerify.VisitSchedulerWork.AnyAsync(
                x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.StopVisit,
                cancellationToken));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.VisitSchedulerWork.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.ProviderOperations.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Notifications.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(cancellationToken, visit.Id);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.ParkingRuleSets.Where(x => x.Id == ruleSet.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task ProcessAsync(IServiceProvider provider, Guid workId, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
        var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == workId, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
            .ProcessAsync(claimed, cancellationToken);
    }

    private async Task ClaimAsync(Guid workId, string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        var pending = await context.VisitSchedulerWork.SingleAsync(x => x.Id == workId, cancellationToken);
        pending.Claim(workerId, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }
}
