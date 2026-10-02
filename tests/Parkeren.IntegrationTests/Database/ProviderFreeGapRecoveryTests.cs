using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderFreeGapRecoveryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Recovery_does_not_duplicate_scheduled_successor_after_free_gap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var nextPaidStart = now.AddMinutes(4);
        var firstPaidEnd = nextPaidStart.AddHours(-1);
        var nextPaidEnd = nextPaidStart.AddHours(1);
        var startAt = firstPaidEnd.AddHours(-1);
        var desiredEndAt = nextPaidEnd.AddHours(1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"gap-recovery-{suffix}", $"GAP-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"GR-{suffix[..2]}-{suffix[2..4]}", $"GR{suffix[..4]}", null);

        var product = new ParkingProviderProduct(
            Guid.NewGuid(),
            $"free-gap-{suffix}",
            $"Free Gap {suffix}",
            null,
            null,
            "Oss",
            now);

        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            desiredEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true),
            product.Id,
            product.ProviderProductId,
            product.Location);
        visit.Activate();

        var remotePredecessor = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(
                vehicle.NormalizedLicensePlate,
                startAt,
                firstPaidEnd,
                "Oss",
                product.ProviderProductId),
            cancellationToken);

        var predecessor = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(),
            visit.Id,
            startAt,
            firstPaidEnd,
            product.ProviderProductId,
            product.Location);
        predecessor.MarkStarting();
        predecessor.MarkActive(remotePredecessor.ProviderActionId, startAt, remotePredecessor.Status);

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            nextPaidStart.AddMinutes(-5));
        work.Claim("free-gap-recovery-test", now);

        var businessZone = TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId);
        var localPaidStart = TimeZoneInfo.ConvertTime(nextPaidStart, businessZone);
        var localPaidEnd = TimeZoneInfo.ConvertTime(nextPaidEnd, businessZone);
        var paidWindows = localPaidStart.Date == localPaidEnd.Date
            ? new[]
            {
                new PaidWindow(
                    localPaidStart.DayOfWeek,
                    TimeOnly.FromDateTime(localPaidStart.DateTime),
                    TimeOnly.FromDateTime(localPaidEnd.DateTime))
            }
            : new[]
            {
                new PaidWindow(
                    localPaidStart.DayOfWeek,
                    TimeOnly.FromDateTime(localPaidStart.DateTime),
                    TimeOnly.MaxValue),
                new PaidWindow(
                    localPaidEnd.DayOfWeek,
                    TimeOnly.MinValue,
                    TimeOnly.FromDateTime(localPaidEnd.DateTime))
            };

        var paidBeforeGap = new ParkingRuleSet(
            Guid.NewGuid(), startAt.AddDays(-1), firstPaidEnd, TimeSpan.FromHours(4), paidWindows);
        var freeGap = new ParkingRuleSet(
            Guid.NewGuid(), firstPaidEnd, nextPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        var paidAfterGap = new ParkingRuleSet(
            Guid.NewGuid(), nextPaidStart, nextPaidEnd, TimeSpan.FromHours(4), paidWindows);
        var freeTail = new ParkingRuleSet(
            Guid.NewGuid(), nextPaidEnd, null, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        paidBeforeGap.AssignProviderProduct(product.Id);
        freeGap.AssignProviderProduct(product.Id);
        paidAfterGap.AssignProviderProduct(product.Id);
        freeTail.AssignProviderProduct(product.Id);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.ParkingProviderProducts.Add(product);
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(predecessor);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.AddRange(paidBeforeGap, freeGap, paidAfterGap, freeTail);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
                ["ParkingProvider:Location"] = "Oss"
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            services.AddSingleton<IParkingProvider>(parkingProvider);
            services.AddLogging();
            await using var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
                var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                    .ProcessAsync(claimed, cancellationToken);
            }

            Guid successorId;
            await using (var verifyContext = fixture.CreateDbContext())
            {
                var actions = await verifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, actions.Count);
                Assert.Equal(ProviderActionState.Completed, actions[0].State);
                Assert.Equal(ProviderActionState.Scheduled, actions[1].State);
                Assert.Equal(nextPaidStart.ToUnixTimeSeconds(), actions[1].PlannedStartAt.ToUnixTimeSeconds());
                successorId = actions[1].Id;
            }

            Assert.Equal(2, (await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken)).Count);

            await using (var recoveryScope = provider.CreateAsyncScope())
                await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                    .RecoverAsync(cancellationToken);

            await using (var recoveryVerifyContext = fixture.CreateDbContext())
            {
                var actionsAfterRecovery = await recoveryVerifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, actionsAfterRecovery.Count);
                Assert.Equal(successorId, actionsAfterRecovery[1].Id);
                Assert.Equal(ProviderActionState.Scheduled, actionsAfterRecovery[1].State);
                Assert.Equal(VisitHealth.Healthy,
                    (await recoveryVerifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
            }

            Assert.Equal(2, (await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken)).Count);
        }
        finally
        {
            await using var cleanupContext = fixture.CreateDbContext();
            await cleanupContext.ProviderOperations
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.VisitSchedulerWork
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ProviderParkingActions
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Notifications
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Visits
                .Where(x => x.Id == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ParkingRuleSets
                .Where(x => x.Id == paidBeforeGap.Id || x.Id == freeGap.Id || x.Id == paidAfterGap.Id || x.Id == freeTail.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ParkingProviderProducts
                .Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Vehicles
                .Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Users
                .Where(x => x.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
