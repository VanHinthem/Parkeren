using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingRuleSetResolverTests
{
    private static ParkingRuleSet Rules(DateTimeOffset from, DateTimeOffset? until = null) =>
        new(Guid.NewGuid(), from, until, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());

    [Fact]
    public void Resolve_selects_rule_set_for_instant()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldRules = Rules(DateTimeOffset.MinValue, boundary);
        var newRules = Rules(boundary);

        Assert.Same(oldRules, ParkingRuleSetResolver.Resolve(new[] { oldRules, newRules }, boundary.AddTicks(-1)));
        Assert.Same(newRules, ParkingRuleSetResolver.Resolve(new[] { oldRules, newRules }, boundary));
    }

    [Fact]
    public void Resolve_rejects_ambiguous_overlapping_rule_sets()
    {
        var first = Rules(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = Rules(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() =>
            ParkingRuleSetResolver.Resolve(new[] { first, second }, second.ValidFrom));
    }

    [Fact]
    public void ValidateNoOverlap_allows_adjacent_rule_sets()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        ParkingRuleSetResolver.ValidateNoOverlap(new[]
        {
            Rules(DateTimeOffset.MinValue, boundary),
            Rules(boundary)
        });
    }

    [Fact]
    public void ValidateNoOverlap_rejects_overlap()
    {
        var first = Rules(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = Rules(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() =>
            ParkingRuleSetResolver.ValidateNoOverlap(new[] { first, second }));
    }
}
