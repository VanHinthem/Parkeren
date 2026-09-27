using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StopVisitCommand(Guid OperationId, Guid VisitId, Guid ActorUserId);

public sealed record StopVisitClaim(
    Visit Visit,
    ProviderOperation? Operation,
    bool IsReplay,
    bool IsAlreadyCompleted);

public interface IStopVisitClaimer
{
    Task<StopVisitClaim> ClaimAsync(
        StopVisitCommand command,
        CancellationToken cancellationToken = default);
}

public interface IStopVisitFinalizer
{
    Task<Visit> CompleteWithoutProviderActionAsync(
        StopVisitClaim claim,
        DateTimeOffset actualEndAt,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderStopPreparation(
    ProviderOperation Operation,
    ProviderParkingAction Action,
    bool IsReplay,
    bool AttemptStartedNow);

public interface IProviderStopStore
{
    Task<ProviderStopPreparation> PrepareAttemptAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default);
}
