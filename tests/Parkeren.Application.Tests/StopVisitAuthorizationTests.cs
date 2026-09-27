using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StopVisitAuthorizationTests
{
    [Fact]
    public void Visitor_can_stop_own_visit()
    {
        var userId = Guid.NewGuid();
        StopVisitAuthorization.Validate(new(userId, UserRole.Visitor, true), CreateVisit(userId));
    }

    [Fact]
    public void Visitor_cannot_stop_another_users_visit()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            StopVisitAuthorization.Validate(new(Guid.NewGuid(), UserRole.Visitor, true), CreateVisit(Guid.NewGuid())));
    }

    [Fact]
    public void Admin_can_stop_another_users_visit()
    {
        StopVisitAuthorization.Validate(new(Guid.NewGuid(), UserRole.Admin, true), CreateVisit(Guid.NewGuid()));
    }

    [Fact]
    public void Inactive_actor_cannot_stop_visit()
    {
        var userId = Guid.NewGuid();
        Assert.Throws<UnauthorizedAccessException>(() =>
            StopVisitAuthorization.Validate(new(userId, UserRole.Visitor, false), CreateVisit(userId)));
    }

    private static Visit CreateVisit(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Visit(
            Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), userId,
            now, now.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
    }
}
