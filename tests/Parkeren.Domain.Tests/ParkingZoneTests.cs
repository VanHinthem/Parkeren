using Parkeren.Domain.Zones;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingZoneTests
{
    [Fact]
    public void Resolve_default_returns_single_applicable_default_zone()
    {
        var now = DateTimeOffset.UtcNow;
        var zone = new ParkingZone(
            Guid.NewGuid(),
            "Oss centrum",
            "OSS_J",
            now.AddDays(-1),
            null,
            true);

        var resolved = ParkingZoneResolver.ResolveDefault([zone], now);

        Assert.Same(zone, resolved);
    }

    [Fact]
    public void Overlapping_default_zones_are_rejected()
    {
        var start = DateTimeOffset.UtcNow;
        var zones = new[]
        {
            new ParkingZone(Guid.NewGuid(), "A", "A", start, null, true),
            new ParkingZone(Guid.NewGuid(), "B", "B", start.AddDays(1), null, true)
        };

        Assert.Throws<InvalidOperationException>(() =>
            ParkingZoneResolver.ValidateDefaultNoOverlap(zones));
    }

    [Fact]
    public void Non_default_zones_may_overlap()
    {
        var start = DateTimeOffset.UtcNow;
        var zones = new[]
        {
            new ParkingZone(Guid.NewGuid(), "Default", "DEF", start, null, true),
            new ParkingZone(Guid.NewGuid(), "Extra", "EXTRA", start, null, false)
        };

        ParkingZoneResolver.ValidateDefaultNoOverlap(zones);
    }

    [Fact]
    public void Open_zone_can_be_closed_once()
    {
        var start = DateTimeOffset.UtcNow;
        var zone = new ParkingZone(Guid.NewGuid(), "Zone", "LOC", start, null, false);
        var until = start.AddDays(1);

        zone.CloseAt(until);

        Assert.Equal(until, zone.ValidUntil);
        Assert.Throws<InvalidOperationException>(() => zone.CloseAt(until.AddDays(1)));
    }
}
