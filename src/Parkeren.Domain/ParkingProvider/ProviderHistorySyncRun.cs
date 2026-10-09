namespace Parkeren.Domain.ParkingProvider;

public enum ProviderHistorySyncRunStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum ProviderHistorySyncRunMode
{
    Bootstrap,
    Incremental,
    Manual
}

/// <summary>
/// One history synchronization attempt. Persistence is introduced in the next slice.
/// </summary>
public sealed class ProviderHistorySyncRun
{
    public Guid Id { get; private set; }
    public string ProviderProductId { get; private set; } = null!;
    public ProviderHistorySyncRunMode Mode { get; private set; }
    public ProviderHistorySyncRunStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int ReadCount { get; private set; }
    public int InsertedCount { get; private set; }
    public int RefreshedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public string? Error { get; private set; }

    private ProviderHistorySyncRun() { }

    public ProviderHistorySyncRun(Guid id, string providerProductId,
        ProviderHistorySyncRunMode mode, DateTimeOffset startedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Run id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Product id is required.", nameof(providerProductId));
        Id = id;
        ProviderProductId = providerProductId.Trim();
        Mode = mode;
        Status = ProviderHistorySyncRunStatus.Running;
        StartedAt = startedAt;
    }

    public void RecordPage(int read, int inserted, int refreshed, int skipped)
    {
        RequireRunning();
        if (read < 0 || inserted < 0 || refreshed < 0 || skipped < 0)
            throw new ArgumentOutOfRangeException(nameof(read), "Counts cannot be negative.");
        ReadCount = checked(ReadCount + read);
        InsertedCount = checked(InsertedCount + inserted);
        RefreshedCount = checked(RefreshedCount + refreshed);
        SkippedCount = checked(SkippedCount + skipped);
    }

    public void Complete(DateTimeOffset at) => Finish(ProviderHistorySyncRunStatus.Succeeded, at, null);
    public void Fail(DateTimeOffset at, string? message) =>
        Finish(ProviderHistorySyncRunStatus.Failed, at, message);
    public void Cancel(DateTimeOffset at) => Finish(ProviderHistorySyncRunStatus.Cancelled, at, null);

    private void Finish(ProviderHistorySyncRunStatus status, DateTimeOffset at, string? message)
    {
        RequireRunning();
        if (at < StartedAt) throw new ArgumentOutOfRangeException(nameof(at));
        Status = status;
        FinishedAt = at;
        Error = string.IsNullOrWhiteSpace(message) ? null : message[..Math.Min(message.Length, 1000)];
    }

    private void RequireRunning()
    {
        if (Status != ProviderHistorySyncRunStatus.Running)
            throw new InvalidOperationException("A completed sync run cannot be modified.");
    }
}
