using Parkeren.Application.Visits;
using Parkeren.Domain.Users;
using Xunit;

namespace Parkeren.Application.Tests;
public sealed class StartVisitAuthorizationTests
{
    [Fact]
    public void Visitor_can_start_for_self_with_assigned_active_vehicle()
    {
        var userId = Guid.NewGuid();
        StartVisitAuthorization.Validate(new(userId, UserRole.Visitor, true), new(userId, true), new(Guid.NewGuid(), true, true));
    }

    [Fact]
    public void Inactive_actor_cannot_start_a_visit()
    {
        Assert.Throws<UnauthorizedAccessException>(() => StartVisitAuthorization.Validate(
            new(Guid.NewGuid(), UserRole.Visitor, false),
            new(Guid.NewGuid(), true),
            new(Guid.NewGuid(), true, true)));
    }

    [Fact]
    public void Inactive_owner_cannot_start_a_visit()
    {
        var userId = Guid.NewGuid();
        Assert.Throws<UnauthorizedAccessException>(() => StartVisitAuthorization.Validate(
            new(userId, UserRole.Visitor, true),
            new(userId, false),
            new(Guid.NewGuid(), true, true)));
    }

    [Fact]
    public void Inactive_vehicle_cannot_start_a_visit()
    {
        var userId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => StartVisitAuthorization.Validate(
            new(userId, UserRole.Visitor, true),
            new(userId, true),
            new(Guid.NewGuid(), false, true)));
    }

    [Fact]
    public void Visitor_cannot_start_for_another_user()
    {
        Assert.Throws<UnauthorizedAccessException>(() => StartVisitAuthorization.Validate(new(Guid.NewGuid(), UserRole.Visitor, true), new(Guid.NewGuid(), true), new(Guid.NewGuid(), true, true)));
    }

    [Fact]
    public void Admin_can_start_for_owner_but_vehicle_must_belong_to_owner()
    {
        Assert.Throws<UnauthorizedAccessException>(() => StartVisitAuthorization.Validate(new(Guid.NewGuid(), UserRole.Admin, true), new(Guid.NewGuid(), true), new(Guid.NewGuid(), true, false)));
    }
}
