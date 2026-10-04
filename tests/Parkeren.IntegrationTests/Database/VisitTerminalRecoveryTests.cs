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
public sealed class VisitTerminalRecoveryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Startup_recovery_rebuilds_missing_overdue_terminal_work_and_makes_it_claimable()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var boundary = now.AddMinutes(-5);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"terminal-recovery-{suffix}", $"TERMINAL-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"TR{suffix[..6]}", $"TR{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-2), boundary,
            new EffectiveParkingPolicySnapshot(null, null, true));
        visit.Activate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>()
                .RecoverAsync(ct);
        }

        Guid workId;
        await using (var verify = fixture.CreateDbContext())
        {
            var work = await verify.VisitSchedulerWork.SingleAsync(x =>
                x.VisitId == visit.Id &&
                x.Type == VisitSchedulerWorkType.StopVisit &&
                x.Status == VisitSchedulerWorkStatus.Pending, ct);

            workId = work.Id;
            Assert.Equal(VisitEndReason.DesiredEndReached, work.EndReason);
            Assert.InRange((work.DueAt - boundary).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            Assert.True(work.DueAt <= now);
        }

        await using (var claimScope = provider.CreateAsyncScope())
        {
            var claimed = await claimScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("terminal-recovery-test", now, ct);
            Assert.NotNull(claimed);
            Assert.Equal(workId, claimed.Id);
            Assert.Equal(VisitSchedulerWorkType.StopVisit, claimed.Type);
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Startup_recovery_replaces_obsolete_pending_terminal_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var boundary = now.AddHours(1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"terminal-replace-{suffix}", $"TERMINAL-REPLACE-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"TX{suffix[..6]}", $"TX{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddMinutes(-30), boundary,
            new EffectiveParkingPolicySnapshot(null, null, true));
        visit.Activate();

        var obsolete = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.StopVisit,
            boundary.AddHours(1), VisitEndReason.MaxVisitElapsedDurationReached);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.VisitSchedulerWork.Add(obsolete);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>()
                .RecoverAsync(ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var works = await verify.VisitSchedulerWork
                .Where(x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.StopVisit)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

            Assert.Equal(2, works.Count);
            Assert.Equal(VisitSchedulerWorkStatus.Cancelled, works.Single(x => x.Id == obsolete.Id).Status);
            var cancellationEvent = Assert.Single(await verify.VisitSchedulerAuditEvents
                .Where(x => x.VisitId == visit.Id &&
                            x.SourceId == obsolete.Id &&
                            x.EventType == "scheduler_work.cancelled")
                .ToListAsync(ct));
            Assert.Equal("terminal_boundary_replanned", cancellationEvent.ReasonCode);

            var replacement = Assert.Single(works, x => x.Status == VisitSchedulerWorkStatus.Pending);
            Assert.Equal(VisitEndReason.DesiredEndReached, replacement.EndReason);
            Assert.InRange((replacement.DueAt - boundary).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        }

        await ClearVisitStateAsync(ct);
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
        await cleanup.Visits.ExecuteDeleteAsync(ct);
    }
}
