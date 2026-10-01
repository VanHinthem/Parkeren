using Parkeren.Domain.ParkingProvider;

namespace Parkeren.Domain.Tests;

public sealed class ProviderDiscrepancyTests
{
    [Fact]
    public void Observe_updates_provider_snapshot_and_last_seen()
    {
        var detectedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var discrepancy = new ProviderDiscrepancy(
            Guid.NewGuid(),
            "action-status:product:action",
            ProviderDiscrepancyType.ProviderActionStatusMismatch,
            Guid.NewGuid(),
            detectedAt,
            providerActionId: " action-1 ",
            providerStatus: "active");

        var observedAt = detectedAt.AddMinutes(5);
        discrepancy.Observe(
            observedAt,
            providerActionId: "action-1",
            providerStatus: " stopped ",
            providerEndAt: observedAt);

        Assert.Equal(ProviderDiscrepancyStatus.Open, discrepancy.Status);
        Assert.Equal("action-1", discrepancy.ProviderActionId);
        Assert.Equal("stopped", discrepancy.ProviderStatus);
        Assert.Equal(observedAt, discrepancy.ProviderEndAt);
        Assert.Equal(observedAt, discrepancy.LastObservedAt);
        Assert.Null(discrepancy.ResolvedAt);
    }

    [Fact]
    public void Resolve_keeps_discrepancy_for_audit()
    {
        var detectedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var discrepancy = new ProviderDiscrepancy(
            Guid.NewGuid(),
            "external-action:product:action",
            ProviderDiscrepancyType.ExternalProviderAction,
            Guid.NewGuid(),
            detectedAt);

        var resolvedAt = detectedAt.AddMinutes(10);
        discrepancy.Resolve(resolvedAt);

        Assert.Equal(ProviderDiscrepancyStatus.Resolved, discrepancy.Status);
        Assert.Equal(detectedAt, discrepancy.DetectedAt);
        Assert.Equal(resolvedAt, discrepancy.ResolvedAt);
        Assert.Throws<InvalidOperationException>(() => discrepancy.Observe(resolvedAt.AddMinutes(1)));
    }
}
