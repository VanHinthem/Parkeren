namespace Parkeren.Domain.ParkingProvider;

public enum ProviderDiscrepancyType
{
    MissingProviderAction,
    ProviderActionStatusMismatch,
    ProviderActionEndMismatch,
    ExternalProviderAction,
    BalanceMismatch
}

public enum ProviderDiscrepancyStatus
{
    Open,
    Resolved
}

public sealed class ProviderDiscrepancy
{
    private ProviderDiscrepancy() { }

    public ProviderDiscrepancy(
        Guid id,
        string key,
        ProviderDiscrepancyType type,
        Guid providerProductId,
        DateTimeOffset detectedAt,
        Guid? visitId = null,
        Guid? providerParkingActionId = null,
        string? providerActionId = null,
        string? providerStatus = null,
        DateTimeOffset? providerStartAt = null,
        DateTimeOffset? providerEndAt = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Discrepancy id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Discrepancy key is required.", nameof(key));
        if (providerProductId == Guid.Empty) throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (visitId == Guid.Empty) throw new ArgumentException("Visit id cannot be empty.", nameof(visitId));
        if (providerParkingActionId == Guid.Empty) throw new ArgumentException("Provider parking action id cannot be empty.", nameof(providerParkingActionId));

        Id = id;
        Key = key.Trim();
        Type = type;
        Status = ProviderDiscrepancyStatus.Open;
        ProviderProductId = providerProductId;
        VisitId = visitId;
        ProviderParkingActionId = providerParkingActionId;
        ProviderActionId = Normalize(providerActionId);
        ProviderStatus = Normalize(providerStatus);
        ProviderStartAt = providerStartAt;
        ProviderEndAt = providerEndAt;
        DetectedAt = detectedAt;
        LastObservedAt = detectedAt;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public ProviderDiscrepancyType Type { get; private set; }
    public ProviderDiscrepancyStatus Status { get; private set; }
    public Guid ProviderProductId { get; private set; }
    public Guid? VisitId { get; private set; }
    public Guid? ProviderParkingActionId { get; private set; }
    public string? ProviderActionId { get; private set; }
    public string? ProviderStatus { get; private set; }
    public DateTimeOffset? ProviderStartAt { get; private set; }
    public DateTimeOffset? ProviderEndAt { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset LastObservedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public uint Version { get; private set; }

    public void Observe(
        DateTimeOffset observedAt,
        string? providerActionId = null,
        string? providerStatus = null,
        DateTimeOffset? providerStartAt = null,
        DateTimeOffset? providerEndAt = null)
    {
        if (Status != ProviderDiscrepancyStatus.Open)
            throw new InvalidOperationException("A resolved discrepancy cannot be observed again.");
        if (observedAt < LastObservedAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));

        ProviderActionId = Normalize(providerActionId) ?? ProviderActionId;
        ProviderStatus = Normalize(providerStatus);
        ProviderStartAt = providerStartAt;
        ProviderEndAt = providerEndAt;
        LastObservedAt = observedAt;
    }

    public void Resolve(DateTimeOffset resolvedAt)
    {
        if (Status != ProviderDiscrepancyStatus.Open)
            throw new InvalidOperationException("Only an open discrepancy can be resolved.");
        if (resolvedAt < LastObservedAt)
            throw new ArgumentOutOfRangeException(nameof(resolvedAt));

        Status = ProviderDiscrepancyStatus.Resolved;
        ResolvedAt = resolvedAt;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
