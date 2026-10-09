using Parkeren.Domain.ParkingProvider;

namespace Parkeren.Domain.Tests;

public sealed class ProviderHistoryPeriodicSyncPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_schedules_product_without_successful_initial_import()
    {
        Assert.False(ProviderHistoryPeriodicSyncPolicy.IsDue(null, null, Now));
        Assert.False(ProviderHistoryPeriodicSyncPolicy.IsDue(null, Now.AddHours(-1), Now));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    public void Respects_minimum_interval_since_successful_import(int minutesAgo, bool expected)
    {
        Assert.Equal(expected, ProviderHistoryPeriodicSyncPolicy.IsDue(
            Now.AddMinutes(-minutesAgo), null, Now));
    }

    [Fact]
    public void Failed_recent_attempt_delays_next_periodic_attempt()
    {
        Assert.False(ProviderHistoryPeriodicSyncPolicy.IsDue(
            Now.AddDays(-1), Now.AddMinutes(-5), Now));

        Assert.True(ProviderHistoryPeriodicSyncPolicy.IsDue(
            Now.AddDays(-1), Now.AddMinutes(-15), Now));
    }

    [Fact]
    public void Earlier_attempt_does_not_override_later_success()
    {
        Assert.False(ProviderHistoryPeriodicSyncPolicy.IsDue(
            Now.AddMinutes(-5), Now.AddHours(-1), Now));
    }

    [Fact]
    public void Future_timestamp_is_not_due()
    {
        Assert.False(ProviderHistoryPeriodicSyncPolicy.IsDue(
            Now.AddMinutes(1), null, Now));
    }
}
