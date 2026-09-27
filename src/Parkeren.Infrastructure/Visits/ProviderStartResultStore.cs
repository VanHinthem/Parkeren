using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartResultStore(ParkerenDbContext dbContext) : IProviderStartResultStore
{
    public async Task RecordResponseAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);
        preparation.Action.CaptureStartResponse(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        preparation.Action.ResetForRetry();
        preparation.Operation.ResetForRetry();
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.SetHealth(VisitHealth.Healthy);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordConfirmedAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);
        preparation.Action.MarkActive(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        preparation.Operation.Succeed(DateTimeOffset.UtcNow);
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");

        // A Stop request can win while the external Start call is in flight.
        // Reload so an already tracked Starting Visit cannot overwrite that Stop claim.
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        if (visit.Status == VisitStatus.Starting)
        {
            visit.Activate();
            visit.SetHealth(VisitHealth.Healthy);
        }
        else if (visit.Status != VisitStatus.Stopping)
        {
            throw new InvalidOperationException($"Provider Start cannot be confirmed for Visit in state {visit.Status}.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        preparation.Operation.Fail(errorCode, DateTimeOffset.UtcNow);
        preparation.Action.MarkFailed();
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        preparation.Action.MarkUnknown();
        preparation.Operation.MarkUnknown(errorCode);
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.SetHealth(VisitHealth.Reconciling);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
