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
public sealed class VisitSchedulerLockingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Two_workers_cannot_claim_the_same_scheduler_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task<VisitSchedulerWork?> ClaimAsync(string workerId)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync(workerId, now, ct);
        }

        var results = await Task.WhenAll(
            ClaimAsync("scheduler-worker-a"),
            ClaimAsync("scheduler-worker-b"));

        Assert.Single(results, x => x is not null);

        await using var verify = fixture.CreateDbContext();
        var persisted = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
        Assert.Equal(VisitSchedulerWorkStatus.Claimed, persisted.Status);
        Assert.Contains(persisted.ClaimedBy, new[] { "scheduler-worker-a", "scheduler-worker-b" });
        Assert.Equal(visit.Id, persisted.VisitId);
    }

    [Fact]
    public async Task Stop_visit_has_priority_when_due_time_matches_other_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, _) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.LongVisitWarning,
            now,
            ct);

        Guid stopWorkId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.VisitSchedulerWork.Add(new VisitSchedulerWork(
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now));
            var stopWork = new VisitSchedulerWork(
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.StopVisit, now);
            stopWorkId = stopWork.Id;
            seed.VisitSchedulerWork.Add(stopWork);
            await seed.SaveChangesAsync(ct);
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
            .ClaimNextDueAsync("scheduler-priority", now, ct);

        Assert.NotNull(claimed);
        Assert.Equal(stopWorkId, claimed!.Id);
        Assert.Equal(VisitSchedulerWorkType.StopVisit, claimed.Type);
    }

    [Fact]
    public async Task Scheduler_claim_and_manual_stop_complete_without_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task ClaimSchedulerAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("scheduler-race", now, ct);
        }

        async Task StopVisitAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, visit.UserId), ct);
        }

        await Task.WhenAll(ClaimSchedulerAsync(), StopVisitAsync()).WaitAsync(TimeSpan.FromSeconds(10), ct);

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Contains(persistedWork.Status, new[]
        {
            VisitSchedulerWorkStatus.Claimed,
            VisitSchedulerWorkStatus.Cancelled
        });
        Assert.Equal(1, await verify.ProviderOperations.CountAsync(
            x => x.VisitId == visit.Id && x.Type == ProviderOperationType.Stop,
            ct));
    }

    [Fact]
    public async Task Scheduler_claim_and_end_time_change_complete_without_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);
        var requestedEndAt = visit.DesiredEndAt!.Value.AddMinutes(-15);
        var command = new ChangeVisitEndTimeCommand(
            Guid.NewGuid(), visit.Id, visit.UserId, requestedEndAt);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task ClaimSchedulerAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("scheduler-end-time-race", now, ct);
        }

        async Task<bool> ChangeEndTimeAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>()
                    .ApplyAsync(command, ct);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var claimTask = ClaimSchedulerAsync();
        var changeTask = ChangeEndTimeAsync();
        await Task.WhenAll(claimTask, changeTask).WaitAsync(TimeSpan.FromSeconds(10), ct);
        var endTimeApplied = await changeTask;

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
        var persistedChange = await verify.VisitEndTimeChanges.SingleAsync(x => x.OperationId == command.OperationId, ct);

        Assert.Equal(VisitSchedulerWorkStatus.Claimed, persistedWork.Status);
        if (endTimeApplied)
        {
            Assert.Equal(requestedEndAt, persistedVisit.DesiredEndAt);
            Assert.Equal(VisitEndTimeChangeResult.Applied, persistedChange.Result);
        }
        else
        {
            Assert.Equal(visit.DesiredEndAt, persistedVisit.DesiredEndAt);
            Assert.Equal(VisitEndTimeChangeResult.Rejected, persistedChange.Result);
        }
    }

    private async Task<(Visit Visit, VisitSchedulerWork Work)> SeedActiveVisitWithWorkAsync(
        DateTimeOffset now,
        VisitSchedulerWorkType type,
        DateTimeOffset dueAt,
        CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            Guid.NewGuid(),
            $"scheduler-{suffix}",
            $"SCHEDULER-{suffix}",
            "hash",
            UserRole.Visitor);
        var plate = $"SC{suffix}".ToUpperInvariant();
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddMinutes(-30),
            now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, type, dueAt);

        await using var seed = fixture.CreateDbContext();
        seed.Users.Add(user);
        seed.Vehicles.Add(vehicle);
        seed.Visits.Add(visit);
        seed.VisitSchedulerWork.Add(work);
        await seed.SaveChangesAsync(ct);
        return (visit, work);
    }

    private ServiceCollection CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }

    private async Task ClearVisitsAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
    }
}
