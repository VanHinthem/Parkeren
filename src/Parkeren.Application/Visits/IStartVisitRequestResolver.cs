namespace Parkeren.Application.Visits;

public sealed record StartVisitRequestContext(
    StartVisitContext StartContext,
    StartVisitProviderContext? ProviderContext);

public interface IStartVisitRequestResolver
{
    Task<StartVisitRequestContext?> ResolveAsync(
        Guid actorUserId,
        Guid ownerUserId,
        Guid vehicleId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default);
}
