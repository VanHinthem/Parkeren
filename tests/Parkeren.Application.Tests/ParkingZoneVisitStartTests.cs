using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class ParkingZoneVisitStartTests
{
    [Fact]
    public void Preparer_snapshots_parking_zone_on_visit()
    {
        var start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var ownerId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var command = new StartVisitCommand(
            Guid.NewGuid(),
            ownerId,
            ownerId,
            vehicleId,
            start,
            start.AddHours(1));
        var context = new StartVisitContext(
            new StartVisitActor(ownerId, UserRole.Visitor, true),
            new StartVisitOwner(ownerId, true),
            new StartVisitVehicle(vehicleId, true, true));
        var policy = new EffectiveParkingPolicy(
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            true,
            false,
            1);
        var rules = new[]
        {
            new ParkingRuleSet(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                null,
                TimeSpan.FromHours(4),
                [new PaidWindow(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(20, 0))])
        };

        var result = new StartVisitPreparer().Prepare(
            command,
            context,
            policy,
            rules,
            start.AddHours(1),
            zoneId);

        Assert.Equal(zoneId, result.Visit.ParkingZoneId);
    }
}
