namespace Parkeren.Domain.ParkingProvider;

/// <summary>
/// Durable progress for one provider product. A page checkpoint is advanced
/// only after its complete import has been committed.
/// </summary>
public sealed class ProviderHistorySyncState
{
    public Guid Id { get; private set; }
    public string ProviderProductId { get; private set; } = null!;
    public int NextPageNumber { get; private set; }
    public int PageSize { get; private set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    private ProviderHistorySyncState() { }

    public ProviderHistorySyncState(Guid id, string providerProductId, int pageSize)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Product id is required.", nameof(providerProductId));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        Id = id;
        ProviderProductId = providerProductId.Trim();
        PageSize = pageSize;
    }

    public void RecordPageCompleted(int pageNumber, DateTimeOffset observedAt)
    {
        if (pageNumber != NextPageNumber)
            throw new InvalidOperationException("History checkpoint must advance sequentially.");
        if (pageNumber == int.MaxValue)
            throw new InvalidOperationException("History checkpoint overflow.");
        NextPageNumber++;
        LastAttemptAt = observedAt;
        LastError = null;
    }

    public void RecordSyncCompleted(DateTimeOffset observedAt)
    {
        LastSuccessfulSyncAt = observedAt;
        LastAttemptAt = observedAt;
        LastError = null;
        NextPageNumber = 0; // A future sync must reread provider history, not skip earlier changes.
    }

    public void RecordFailure(DateTimeOffset observedAt, string message)
    {
        LastAttemptAt = observedAt;
        LastError = string.IsNullOrWhiteSpace(message) ? "History import failed." : message[..Math.Min(message.Length, 1000)];
    }
}
