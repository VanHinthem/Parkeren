using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitStartTerminalWorkTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Saving_active_visit_ensures_terminal_stop_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = DateTimeOffset.UtcNow;
        var desiredEndAt = startAt.AddHours(2);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"terminal-start-{suffix}", $"TERMINAL-START-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"TS{suffix[..6]}", $"TS{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            desiredEndAt,
            new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            startAt.AddDays(-1),
            startAt.AddDays(1),
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ParkingRuleSets.Add(rules);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var persistedVisit = await dbContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitStartStore>()
                .SaveAsync(persistedVisit, ct);
        }

        await using var verify = fixture.CreateDbContext();
        var terminalWork = await verify.VisitSchedulerWork.SingleAsync(
            x => x.VisitId == visit.Id &&
                 x.Type == VisitSchedulerWorkType.StopVisit &&
                 x.Status == VisitSchedulerWorkStatus.Pending,
            ct);

        Assert.InRange(
            (terminalWork.DueAt - desiredEndAt).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitEndReason.DesiredEndReached, terminalWork.EndReason);
    }

    [Theory]
    [InlineData(2026, 10, 9, 10)] // Friday evening -> Saturday morning
    [InlineData(2026, 10, 11, 12)] // Free Sunday evening -> Monday morning
    public async Task Saving_open_ended_evening_visit_schedules_next_paid_morning_coverage(
        int year, int month, int startDay, int paidDay)
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = new DateTimeOffset(year, month, startDay, 18, 16, 0, TimeSpan.Zero); // 20:16 Amsterdam
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"overnight-{suffix}", $"OVERNIGHT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ON{suffix[..6]}", $"ON{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            null,
            new EffectiveParkingPolicySnapshot(null, null, true));
        visit.Activate();

        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            null,
            TimeSpan.FromHours(4),
            new[]
            {
                new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(20, 0))
            });

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ParkingRuleSets.Add(rules);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var persistedVisit = await dbContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitStartStore>()
                .SaveAsync(persistedVisit, ct);
        }

        await using var verify = fixture.CreateDbContext();
        var work = await verify.VisitSchedulerWork.SingleAsync(
            x => x.VisitId == visit.Id &&
                 x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                 x.Status == VisitSchedulerWorkStatus.Pending,
            ct);

        Assert.Equal(new DateTimeOffset(year, month, paidDay, 6, 55, 0, TimeSpan.Zero), work.DueAt);
        Assert.False(await verify.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id, ct));
    }

    private ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private async Task ClearVisitStateAsync(CancellationToken ct)
    {
        await using var cleanup = fixture.CreateDbContext();
        await cleanup.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await cleanup.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await cleanup.ProviderOperations.ExecuteDeleteAsync(ct);
        await cleanup.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await cleanup.Notifications.ExecuteDeleteAsync(ct);
        await cleanup.NotificationEvents.ExecuteDeleteAsync(ct);
        await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct);
        await cleanup.Visits.ExecuteDeleteAsync(ct);
        await cleanup.PaidWindows.ExecuteDeleteAsync(ct);
        await cleanup.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
        await cleanup.ParkingRuleSets.ExecuteDeleteAsync(ct);
    }
}