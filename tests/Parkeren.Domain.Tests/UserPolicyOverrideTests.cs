using Parkeren.Domain.Policies;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class UserPolicyOverrideTests
{
    [Fact]
    public void Overrides_are_applied_per_field()
    {
        var defaults = new DefaultParkingPolicy(
            Guid.NewGuid(),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            allowVisitExtension: true,
            allowOpenEndedVisits: false,
            maxConcurrentVisits: 1);
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        policyOverride.SetOverrides(
            TimeSpan.FromHours(2),
            maxVisitElapsedDuration: null,
            allowVisitExtension: false,
            allowOpenEndedVisits: null,
            maxConcurrentVisits: 3);

        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        Assert.Equal(TimeSpan.FromHours(2), effective.MaxPaidParkingDuration);
        Assert.Equal(TimeSpan.FromHours(8), effective.MaxVisitElapsedDuration);
        Assert.False(effective.AllowVisitExtension);
        Assert.False(effective.AllowOpenEndedVisits);
        Assert.Equal(3, effective.MaxConcurrentVisits);
        Assert.True(policyOverride.HasAnyOverride);
    }

    [Fact]
    public void Null_override_values_return_all_fields_to_defaults()
    {
        var defaults = new DefaultParkingPolicy(
            Guid.NewGuid(),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            allowVisitExtension: true,
            allowOpenEndedVisits: false,
            maxConcurrentVisits: 1);
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());
        policyOverride.SetOverrides(TimeSpan.FromHours(2), TimeSpan.FromHours(6), false, true, 2);

        policyOverride.SetOverrides(null, null, null, null, null);
        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        Assert.Equal(ParkingPolicyResolver.Resolve(defaults, null), effective);
        Assert.False(policyOverride.HasAnyOverride);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Max_concurrent_visits_override_must_be_positive(int value)
    {
        var policyOverride = new UserPolicyOverride(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policyOverride.SetOverrides(null, null, null, null, value));
    }
}
