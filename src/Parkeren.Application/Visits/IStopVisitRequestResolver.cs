namespace Parkeren.Application.Visits;

public interface IStopVisitRequestResolver
{
    Task<StopVisitContext?> ResolveAsync(
        Guid actorUserId,
        Guid visitId,
        CancellationToken cancellationToken = default);
}
