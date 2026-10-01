using Parkeren.Domain.Policies;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class VisitDurationPolicyValidatorTests
{
    [Fact]
    public void Visit_has_no_elapsed_limit_when_policy_does_not_define_one()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true);
        var start = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var result = VisitDurationPolicyValidator.Validate(policy, start, start.AddDays(2));

        Assert.True(result.IsAllowed);
        Assert.Null(result.RemainingElapsedDuration);
    }

    [Fact]
    public void Visit_exactly_at_elapsed_limit_is_allowed()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var start = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var result = VisitDurationPolicyValidator.Validate(policy, start, start.AddHours(8));

        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.RemainingElapsedDuration);
    }

    [Fact]
    public void Visit_beyond_elapsed_limit_is_rejected_even_when_part_is_free_time()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var start = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var result = VisitDurationPolicyValidator.Validate(policy, start, start.AddHours(9));

        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.RemainingElapsedDuration);
    }
}
