using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderActionPlannerTests
{
    [Fact]
    public void Paid_period_longer_than_provider_limit_is_split()
    {
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

        var actions = ProviderActionPlanner.Plan(
            start,
            start.AddHours(9),
            TimeSpan.FromHours(4));

        Assert.Equal(3, actions.Count);
        Assert.Equal(TimeSpan.FromHours(4), actions[0].End - actions[0].Start);
        Assert.Equal(TimeSpan.FromHours(4), actions[1].End - actions[1].Start);
        Assert.Equal(TimeSpan.FromHours(1), actions[2].End - actions[2].Start);
        Assert.Equal(start.AddHours(9), actions[2].End);
    }

    [Fact]
    public void Paid_period_within_provider_limit_needs_one_action()
    {
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

        var actions = ProviderActionPlanner.Plan(
            start,
            start.AddHours(3),
            TimeSpan.FromHours(4));

        Assert.Single(actions);
        Assert.Equal(start, actions[0].Start);
        Assert.Equal(start.AddHours(3), actions[0].End);
    }
}
