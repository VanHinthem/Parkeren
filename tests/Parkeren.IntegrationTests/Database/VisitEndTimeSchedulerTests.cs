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
        await ClearVisitStateAsync(ct);

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

        await using var provider = BuildServices();
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

        // A later extension may need work at the same boundary again.
        var replacement = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, work.DueAt);
        verify.VisitSchedulerWork.Add(replacement);
        await verify.SaveChangesAsync(ct);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, replacement.Status);
    }

    [Fact]
    public async Task Extending_visit_schedules_continuation_when_new_paid_coverage_is_needed()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        var currentEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(3);
        var userName = $"extend-paid-{Guid.NewGuid():N}";
        var plate = $"EP{Guid.NewGuid():N}"[..8];
        var user = new User(Guid.NewGuid(), userName, userName.ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, currentEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, currentEndAt);
        action.MarkStarting();
        action.MarkActive("provider-paid", startAt, "active");

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.ParkingRuleSets.Add(CreateWednesdayRuleSet(startAt));
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        var operationId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>()
                .ApplyAsync(new ChangeVisitEndTimeCommand(
                    operationId, visit.Id, user.Id, requestedEndAt), ct);
        }

        await using var verify = fixture.CreateDbContext();
        var work = await verify.VisitSchedulerWork.SingleAsync(
            x => x.VisitId == visit.Id &&
                 x.Type == VisitSchedulerWorkType.ContinueProviderCoverage,
            ct);

        Assert.Equal(VisitSchedulerWorkStatus.Pending, work.Status);
        Assert.Equal(currentEndAt.AddMinutes(-5), work.DueAt);
        Assert.Equal(requestedEndAt,
            (await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct)).DesiredEndAt);
    }

    [Fact]
    public async Task Extending_visit_into_free_time_does_not_schedule_provider_coverage()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        // 17:00 UTC is 19:00 Europe/Amsterdam on this date. The current
        // provider action ends at 20:00 local, after which parking is free.
        var startAt = new DateTimeOffset(2026, 9, 30, 17, 0, 0, TimeSpan.Zero);
        var currentEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(3);
        var userName = $"extend-free-{Guid.NewGuid():N}";
        var plate = $"EF{Guid.NewGuid():N}"[..8];
        var user = new User(Guid.NewGuid(), userName, userName.ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, currentEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, currentEndAt);
        action.MarkStarting();
        action.MarkActive("provider-free", startAt, "active");

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.ParkingRuleSets.Add(CreateWednesdayRuleSet(startAt));
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>()
                .ApplyAsync(new ChangeVisitEndTimeCommand(
                    Guid.NewGuid(), visit.Id, user.Id, requestedEndAt), ct);
        }

        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.VisitSchedulerWork.AnyAsync(
            x => x.VisitId == visit.Id &&
                 x.Type == VisitSchedulerWorkType.ContinueProviderCoverage,
            ct));
    }

    [Fact]
    public async Task Replaying_extension_does_not_duplicate_continuation_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        var currentEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(3);
        var userName = $"extend-replay-{Guid.NewGuid():N}";
        var plate = $"ER{Guid.NewGuid():N}"[..8];
        var user = new User(Guid.NewGuid(), userName, userName.ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, currentEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, currentEndAt);
        action.MarkStarting();
        action.MarkActive("provider-replay", startAt, "active");

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.ParkingRuleSets.Add(CreateWednesdayRuleSet(startAt));
            await seed.SaveChangesAsync(ct);
        }

        var operationId = Guid.NewGuid();
        await using var provider = BuildServices();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>()
                .ApplyAsync(new ChangeVisitEndTimeCommand(
                    operationId, visit.Id, user.Id, requestedEndAt), ct);
        }

        await using var verify = fixture.CreateDbContext();
        Assert.Single(await verify.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        x.Type == VisitSchedulerWorkType.ContinueProviderCoverage)
            .ToListAsync(ct));
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

    private static ParkingRuleSet CreateWednesdayRuleSet(DateTimeOffset reference) =>
        new(
            Guid.NewGuid(),
            reference.AddDays(-1),
            reference.AddDays(1),
            TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);

    private async Task ClearVisitStateAsync(CancellationToken ct)
    {
        await using var cleanup = fixture.CreateDbContext();
        await cleanup.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await cleanup.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await cleanup.ProviderOperations.ExecuteDeleteAsync(ct);
        await cleanup.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await cleanup.Notifications.ExecuteDeleteAsync(ct);
        await cleanup.NotificationEvents.ExecuteDeleteAsync(ct);
        await cleanup.Visits.ExecuteDeleteAsync(ct);
        await cleanup.PaidWindows.ExecuteDeleteAsync(ct);
        await cleanup.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
        await cleanup.ParkingRuleSets.ExecuteDeleteAsync(ct);
    }
}
