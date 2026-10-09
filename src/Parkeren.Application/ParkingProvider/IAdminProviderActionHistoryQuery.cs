using Parkeren.Domain.Visits;

namespace Parkeren.Application.ParkingProvider;

public sealed record AdminProviderActionHistoryFilter(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string? ProviderProductId = null,
    ProviderActionState? State = null,
    ProviderActionOrigin? Origin = null,
    Guid? AssignedUserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Until = null,
    bool OldestFirst = false,
    bool? HasOpenDiscrepancy = null);

public sealed record AdminProviderActionHistoryRow(
    Guid Id,
    string? ProviderActionId,
    Guid? VisitId,
    string? ProviderProductId,
    string? LicensePlate,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    decimal? ProviderCostAmount,
    ProviderActionState State,
    ProviderActionOrigin Origin,
    Guid? AssignedUserId,
    ProviderActionAssignmentSource AssignmentSource,
    ProviderHistoryStatus HistoryStatus,
    string? Username);

public sealed record AdminProviderActionHistoryPage(
    IReadOnlyList<AdminProviderActionHistoryRow> Items,
    int Page,
    int PageSize,
    int TotalCount);

public interface IAdminProviderActionHistoryQuery
{
    Task<AdminProviderActionHistoryPage> GetAsync(
        AdminProviderActionHistoryFilter filter,
        CancellationToken cancellationToken);
}
