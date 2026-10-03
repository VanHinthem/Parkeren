namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderActionHistoryRecord(
    string ProviderActionId,
    string Status,
    DateTimeOffset ActualStartAt,
    DateTimeOffset ActualEndAt,
    decimal? ProviderCostAmount,
    string? Currency);

public sealed record ProviderActionHistoryPage(
    IReadOnlyList<ProviderActionHistoryRecord> Records,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public bool HasMore => (PageNumber + 1) * PageSize < TotalCount;
}

public interface IProviderActionHistoryReader
{
    Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
        string providerProductId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}