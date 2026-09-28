using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitEndTimeSchedulerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Shortening_visit_cancels_future_scheduler_work_atomically()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var cleanup = fixture.CreateDbContext())
        {
            await cleanup.VisitSchedulerWork.ExecuteDeleteAsync(ct);
            await cleanup.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
            await cleanup.ProviderOperations.ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.ExecuteDeleteAsync(ct);
            await cleanup.Visits.ExecuteDeleteAsync(ct);
            await cleanup.PaidWindows.ExecuteDeleteAsync(ct);
            await cleanup.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
            await cleanup.ParkingRuleSets.ExecuteDeleteAsync(ct);
        }
        var now = DateTimeOffset.UtcNow;
        var userName = $"scheduler-user-{Guid.NewGuid():N}";
        var plate = $"SC{Guid.NewGuid():N}"[..8];
        var user = new User(Guid.NewGuid(), userName, userName.ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now, now.AddHours(2), new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(90));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.VisitSchedulerWork.Add(work);
            seed.ParkingRuleSets.Add(new ParkingRuleSet(Guid.NewGuid(), now.AddDays(-1),
                now.AddDays(1), TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            await seed.SaveChangesAsync(ct);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
            await changer.ApplyAsync(new ChangeVisitEndTimeCommand(
                Guid.NewGuid(), visit.Id, user.Id, now.AddHours(1)), ct);
        }

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled,
            (await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct)).Status);
        var persistedEnd = (await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct)).DesiredEndAt;
        Assert.NotNull(persistedEnd);
        Assert.InRange((persistedEnd.Value - now.AddHours(1)).Duration(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }
}
