using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderDiscrepancyObservation(
    string Key,
    ProviderDiscrepancyType Type,
    Guid ProviderProductId,
    DateTimeOffset ObservedAt,
    Guid? VisitId = null,
    Guid? ProviderParkingActionId = null,
    string? ProviderActionId = null,
    string? ProviderStatus = null,
    DateTimeOffset? ProviderStartAt = null,
    DateTimeOffset? ProviderEndAt = null);

public sealed record ProviderDiscrepancySummary(
    Guid Id,
    string Key,
    ProviderDiscrepancyType Type,
    ProviderDiscrepancyStatus Status,
    Guid ProviderProductId,
    string ProviderProductName,
    string ProviderProductExternalId,
    Guid? VisitId,
    Guid? ProviderParkingActionId,
    ProviderActionState? LocalProviderActionState,
    DateTimeOffset? LocalPlannedEndAt,
    string? ProviderActionId,
    string? ProviderStatus,
    DateTimeOffset? ProviderStartAt,
    DateTimeOffset? ProviderEndAt,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt);

public interface IProviderDiscrepancyService
{
    Task<ProviderDiscrepancySummary> ObserveAsync(
        ProviderDiscrepancyObservation observation,
        CancellationToken cancellationToken = default);

    Task<bool> ResolveAsync(
        string key,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderDiscrepancySummary>> GetAsync(
        bool includeResolved,
        CancellationToken cancellationToken = default);
}
