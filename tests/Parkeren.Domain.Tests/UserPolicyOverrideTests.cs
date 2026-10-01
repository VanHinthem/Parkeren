using Parkeren.Domain.Policies;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class UserPolicyOverrideTests
{
    private static DefaultParkingPolicy CreateDefaults() => new(
        Guid.NewGuid(),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        allowVisitExtension: true,
        allowOpenEndedVisits: false,
        maxConcurrentVisits: 1);

    [Fact]
    public void Inherit_uses_default_duration_values()
    {
        var defaults = CreateDefaults();
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        Assert.Equal(TimeSpan.FromHours(4), effective.MaxPaidParkingDuration);
        Assert.Equal(TimeSpan.FromHours(8), effective.MaxVisitElapsedDuration);
        Assert.False(policyOverride.HasAnyOverride);
    }

    [Fact]
    public void Value_mode_applies_explicit_duration_overrides()
    {
        var defaults = CreateDefaults();
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        policyOverride.SetOverrides(
            PolicyDurationOverrideMode.Value,
            TimeSpan.FromHours(2),
            PolicyDurationOverrideMode.Value,
            TimeSpan.FromHours(6),
            allowVisitExtension: false,
            allowOpenEndedVisits: true,
            maxConcurrentVisits: 3);

        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        Assert.Equal(TimeSpan.FromHours(2), effective.MaxPaidParkingDuration);
        Assert.Equal(TimeSpan.FromHours(6), effective.MaxVisitElapsedDuration);
        Assert.False(effective.AllowVisitExtension);
        Assert.True(effective.AllowOpenEndedVisits);
        Assert.Equal(3, effective.MaxConcurrentVisits);
        Assert.True(policyOverride.HasAnyOverride);
    }

    [Fact]
    public void Unlimited_mode_overrides_limited_default_with_null_effective_duration()
    {
        var defaults = CreateDefaults();
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        policyOverride.SetOverrides(
            PolicyDurationOverrideMode.Unlimited,
            null,
            PolicyDurationOverrideMode.Unlimited,
            null,
            allowVisitExtension: null,
            allowOpenEndedVisits: null,
            maxConcurrentVisits: null);

        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        Assert.Null(effective.MaxPaidParkingDuration);
        Assert.Null(effective.MaxVisitElapsedDuration);
        Assert.True(policyOverride.HasAnyOverride);
    }

    [Fact]
    public void Returning_duration_modes_to_inherit_restores_defaults()
    {
        var defaults = CreateDefaults();
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());
        policyOverride.SetOverrides(
            PolicyDurationOverrideMode.Unlimited,
            null,
            PolicyDurationOverrideMode.Value,
            TimeSpan.FromHours(6),
            false,
            true,
            2);

        policyOverride.SetOverrides(
            PolicyDurationOverrideMode.Inherit,
            null,
            PolicyDurationOverrideMode.Inherit,
            null,
            null,
            null,
            null);

        Assert.Equal(ParkingPolicyResolver.Resolve(defaults, null), ParkingPolicyResolver.Resolve(defaults, policyOverride));
        Assert.False(policyOverride.HasAnyOverride);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Value_mode_requires_positive_duration(int minutes)
    {
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policyOverride.SetOverrides(
                PolicyDurationOverrideMode.Value,
                TimeSpan.FromMinutes(minutes),
                PolicyDurationOverrideMode.Inherit,
                null,
                null,
                null,
                null));
    }

    [Theory]
    [InlineData(PolicyDurationOverrideMode.Inherit)]
    [InlineData(PolicyDurationOverrideMode.Unlimited)]
    public void Non_value_duration_mode_rejects_duration_value(PolicyDurationOverrideMode mode)
    {
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() =>
            policyOverride.SetOverrides(
                mode,
                TimeSpan.FromHours(1),
                PolicyDurationOverrideMode.Inherit,
                null,
                null,
                null,
                null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Max_concurrent_visits_override_must_be_positive(int value)
    {
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policyOverride.SetOverrides(
                PolicyDurationOverrideMode.Inherit,
                null,
                PolicyDurationOverrideMode.Inherit,
                null,
                null,
                null,
                value));
    }
}
