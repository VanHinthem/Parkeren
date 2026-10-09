using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests.ParkingProvider;

public sealed class ProviderHistoryImportPlanTests
{
    private static ProviderActionHistoryRecord Record(string id, decimal? cost = 0.25m) =>
        new(id, "COMPLETED", DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow, cost, "EUR", "AB12CD", "OSS Zone J");

    [Fact]
    public void Identical_duplicate_action_ids_are_collapsed()
    {
        var record = Record("action-1");
        var page = new ProviderActionHistoryPage([record, record], 0, 10, 2);

        var result = ProviderHistoryImportPlan.Prepare(page);

        Assert.Single(result);
        Assert.Equal("action-1", result[0].ProviderActionId);
    }

    [Fact]
    public void Conflicting_duplicate_action_ids_are_rejected()
    {
        var record = Record("action-2");
        var page = new ProviderActionHistoryPage([record, record with { ProviderCostAmount = 0.50m }], 0, 10, 2);

        Assert.Throws<InvalidOperationException>(() => ProviderHistoryImportPlan.Prepare(page));
    }

    [Fact]
    public void Missing_provider_action_id_is_rejected()
    {
        var page = new ProviderActionHistoryPage([Record(" ")], 0, 10, 1);
        Assert.Throws<InvalidOperationException>(() => ProviderHistoryImportPlan.Prepare(page));
    }

    [Fact]
    public void Distinct_action_ids_remain_distinct()
    {
        var page = new ProviderActionHistoryPage([Record("action-a"), Record("action-b")], 0, 10, 2);
        Assert.Equal(2, ProviderHistoryImportPlan.Prepare(page).Count);
    }
}
