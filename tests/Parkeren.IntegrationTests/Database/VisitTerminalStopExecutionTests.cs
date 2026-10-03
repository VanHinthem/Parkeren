using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitTerminalStopExecutionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Scheduled_terminal_stop_persists_reason_and_functional_boundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddHours(-2);
        var boundary = now.AddMinutes(-5);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"terminal-stop-{suffix}", $"TERMINAL-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"TB{suffix[..6]}", $"TB{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            boundary,
            new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.StopVisit,
            boundary,
            VisitEndReason.DesiredEndReached);
        work.Claim("terminal-stop-test", now);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.VisitSchedulerWork.Add(work);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var claimed = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            var operation = await verify.ProviderOperations.SingleAsync(x => x.OperationId == work.Id, ct);

            Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
            Assert.Equal(VisitEndReason.DesiredEndReached, persistedVisit.EndReason);
            Assert.NotNull(persistedVisit.ActualEndAt);
            Assert.InRange(
                (persistedVisit.ActualEndAt.Value - now).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5));
            Assert.True(persistedVisit.ActualEndAt > boundary);
            Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedWork.Status);
            Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
            Assert.Equal(persistedVisit.ActualEndAt, operation.CompletedAt);
            Assert.False(await verify.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id, ct));
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Stop_claim_preserves_pending_provider_action_reconciliation()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(-20);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-stop-{suffix}", $"HISTORY-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HS{suffix[..6]}", $"HS{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, now.AddHours(2), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-action", startAt);
        var reconciliation = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddMinutes(1), providerParkingActionId: action.Id);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(reconciliation);
            await seed.SaveChangesAsync(ct);
        }

        await using (var scope = BuildServices().CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == reconciliation.Id, ct);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, persistedWork.Status);
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Manual_stop_keeps_manual_reason_and_supplied_actual_end()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var desiredEndAt = startAt.AddHours(2);
        var actualEndAt = startAt.AddMinutes(20);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"manual-stop-{suffix}", $"MANUAL-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"MB{suffix[..6]}", $"MB{suffix[..6]}", null);
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

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        StopVisitClaim claim;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            claim = await claimScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), ct);
        }

        await using (var finalizeScope = provider.CreateAsyncScope())
        {
            await finalizeScope.ServiceProvider.GetRequiredService<IStopVisitFinalizer>()
                .CompleteWithoutProviderActionAsync(claim, actualEndAt, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);

            Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
            Assert.Equal(VisitEndReason.ManualStop, persistedVisit.EndReason);
            Assert.NotNull(persistedVisit.ActualEndAt);
            Assert.InRange(
                (persistedVisit.ActualEndAt.Value - actualEndAt).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
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
        services.AddSingleton<IParkingProvider, NoOpParkingProvider>();
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

    private sealed class NoOpParkingProvider : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderCategory>>(Array.Empty<ProviderCategory>());

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>(Array.Empty<Parkeren.Application.ParkingProvider.ProviderParkingAction>());

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
