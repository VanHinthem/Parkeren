using Parkeren.Domain.Policies;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingPolicyValidatorTests
{
    private static EffectiveParkingPolicy Policy(TimeSpan maxPaid) =>
        new(maxPaid, null, true);

    [Fact]
    public void Free_elapsed_time_does_not_affect_paid_duration_limit()
    {
        var result = ParkingPolicyValidator.ValidatePaidDuration(
            Policy(TimeSpan.FromHours(4)),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(2));

        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.FromHours(2), result.RemainingPaidDuration);
    }

    [Fact]
    public void Request_exceeding_remaining_paid_duration_is_rejected()
    {
        var result = ParkingPolicyValidator.ValidatePaidDuration(
            Policy(TimeSpan.FromHours(4)),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(1.5));

        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.FromHours(1), result.RemainingPaidDuration);
    }

    [Fact]
    public void Exhausted_limit_has_zero_remaining_duration()
    {
        var result = ParkingPolicyValidator.ValidatePaidDuration(
            Policy(TimeSpan.FromHours(4)),
            TimeSpan.FromHours(5),
            TimeSpan.Zero);

        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.RemainingPaidDuration);
    }
}
