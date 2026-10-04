using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthenticationLifecycleTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_active_user_cannot_log_in_or_create_a_session(bool archived)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"lifecycle-login-{suffix}";
        const string pin = "123456";
        var user = new User(Guid.NewGuid(), username, username.ToUpperInvariant(), "pending", UserRole.Visitor);
        user.ChangePinHash(new PasswordHasher<User>().HashPassword(user, pin));
        if (archived)
            user.Archive();
        else
            user.Deactivate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            await using var provider = services.BuildServiceProvider();
            var authentication = provider.GetRequiredService<IAuthenticationService>();

            Assert.Null(await authentication.LoginAsync(username, pin, ct));

            await using var verify = fixture.CreateDbContext();
            Assert.False(await verify.UserSessions.AnyAsync(x => x.UserId == user.Id, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserSessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
        }
    }
}