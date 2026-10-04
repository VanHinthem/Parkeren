using Parkeren.Domain.Users;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class UserTests
{
    [Fact]
    public void Archived_user_cannot_be_reactivated()
    {
        var user = new User(Guid.NewGuid(), "visitor", "VISITOR", "hash", UserRole.Visitor);

        user.Archive();

        Assert.Equal(UserStatus.Archived, user.Status);
        Assert.False(user.IsActive);
        Assert.Throws<InvalidOperationException>(user.Activate);
    }
}