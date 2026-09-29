using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class EffectiveParkingPolicySnapshotTests
{
    [Fact]
    public void Snapshot_keeps_effective_values_independent_of_later_defaults()
    {
        var defaults = new DefaultParkingPolicy(Guid.NewGuid(), TimeSpan.FromHours(8), TimeSpan.FromHours(12), true);
        var effective = ParkingPolicyResolver.Resolve(defaults, null);
        var snapshot = EffectiveParkingPolicySnapshot.Capture(effective);

        Assert.Equal(TimeSpan.FromHours(8), snapshot.MaxPaidParkingDuration);
        Assert.Equal(TimeSpan.FromHours(12), snapshot.MaxVisitElapsedDuration);
        Assert.True(snapshot.AllowVisitExtension);
        Assert.True(snapshot.AllowOpenEndedVisits);
        Assert.Equal(effective, snapshot.ToEffectivePolicy());
    }
}
