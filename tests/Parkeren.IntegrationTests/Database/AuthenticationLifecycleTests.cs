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
            await using var provider = CreateServices();
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

    [Fact]
    public async Task Incorrect_pin_cannot_log_in_or_create_a_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"lifecycle-invalid-pin-{suffix}";
        var user = CreateUser(username, "123456", UserRole.Visitor);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var provider = CreateServices();
            var authentication = provider.GetRequiredService<IAuthenticationService>();

            Assert.Null(await authentication.LoginAsync(username, "654321", ct));

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

    [Fact]
    public async Task Login_persists_pin_and_session_token_only_as_hashes()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"lifecycle-secret-storage-{suffix}";
        const string pin = "864209";
        var user = CreateUser(username, pin, UserRole.Visitor);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            string sessionToken;
            await using (var provider = CreateServices())
            {
                var authentication = provider.GetRequiredService<IAuthenticationService>();
                var login = await authentication.LoginAsync(username, pin, ct);
                Assert.NotNull(login);
                sessionToken = login.SessionToken;
            }

            await using var verify = fixture.CreateDbContext();
            var persistedUser = await verify.Users.SingleAsync(x => x.Id == user.Id, ct);
            var persistedSession = await verify.UserSessions.SingleAsync(x => x.UserId == user.Id, ct);

            Assert.NotEqual(pin, persistedUser.PinHash);
            Assert.NotEqual(sessionToken, persistedSession.TokenHash);
            Assert.DoesNotContain(sessionToken, persistedSession.TokenHash, StringComparison.Ordinal);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserSessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Existing_session_cannot_authenticate_after_user_is_deactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"lifecycle-deactivate-{suffix}";
        const string pin = "123456";
        var user = CreateUser(username, pin, UserRole.Visitor);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            string sessionToken;
            await using (var provider = CreateServices())
            await using (var scope = provider.CreateAsyncScope())
            {
                var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var login = await authentication.LoginAsync(username, pin, ct);
                Assert.NotNull(login);
                sessionToken = login.SessionToken;
            }

            await using (var deactivate = fixture.CreateDbContext())
            {
                var persistedUser = await deactivate.Users.SingleAsync(x => x.Id == user.Id, ct);
                persistedUser.Deactivate();
                await deactivate.SaveChangesAsync(ct);
            }

            await using var verifyProvider = CreateServices();
            await using var verifyScope = verifyProvider.CreateAsyncScope();
            var verifyAuthentication = verifyScope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            Assert.Null(await verifyAuthentication.AuthenticateAsync(sessionToken, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserSessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Admin_can_revoke_all_existing_user_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        const string pin = "123456";
        var admin = CreateUser($"lifecycle-admin-{suffix}", pin, UserRole.Admin);
        var user = CreateUser($"lifecycle-revoke-{suffix}", pin, UserRole.Visitor);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            string firstToken;
            string secondToken;
            await using (var provider = CreateServices())
            await using (var scope = provider.CreateAsyncScope())
            {
                var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var firstLogin = await authentication.LoginAsync(user.Username, pin, ct);
                var secondLogin = await authentication.LoginAsync(user.Username, pin, ct);
                Assert.NotNull(firstLogin);
                Assert.NotNull(secondLogin);
                firstToken = firstLogin.SessionToken;
                secondToken = secondLogin.SessionToken;
            }

            await using (var provider = CreateServices())
            await using (var scope = provider.CreateAsyncScope())
            {
                var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                Assert.True(await authentication.RevokeAllSessionsAsync(admin.Id, user.Id, ct));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var sessions = await verify.UserSessions.AsNoTracking()
                    .Where(x => x.UserId == user.Id)
                    .ToListAsync(ct);
                Assert.Equal(2, sessions.Count);
                Assert.All(sessions, session => Assert.NotNull(session.RevokedAt));
            }

            await using var verifyProvider = CreateServices();
            await using var verifyScope = verifyProvider.CreateAsyncScope();
            var verifyAuthentication = verifyScope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            Assert.Null(await verifyAuthentication.AuthenticateAsync(firstToken, ct));
            Assert.Null(await verifyAuthentication.AuthenticateAsync(secondToken, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.UserSessions.Where(x => x.UserId == admin.Id || x.UserId == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == user.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Logout_revokes_only_the_selected_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"lifecycle-logout-{suffix}";
        const string pin = "123456";
        var user = CreateUser(username, pin, UserRole.Visitor);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            string firstToken;
            string secondToken;
            await using (var provider = CreateServices())
            await using (var scope = provider.CreateAsyncScope())
            {
                var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var firstLogin = await authentication.LoginAsync(username, pin, ct);
                var secondLogin = await authentication.LoginAsync(username, pin, ct);
                Assert.NotNull(firstLogin);
                Assert.NotNull(secondLogin);
                firstToken = firstLogin.SessionToken;
                secondToken = secondLogin.SessionToken;
            }

            await using (var provider = CreateServices())
            await using (var scope = provider.CreateAsyncScope())
            {
                var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                await authentication.LogoutAsync(firstToken, ct);
            }

            await using var verifyProvider = CreateServices();
            await using var verifyScope = verifyProvider.CreateAsyncScope();
            var verifyAuthentication = verifyScope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            Assert.Null(await verifyAuthentication.AuthenticateAsync(firstToken, ct));
            Assert.NotNull(await verifyAuthentication.AuthenticateAsync(secondToken, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserSessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
        }
    }

    private static User CreateUser(string username, string pin, UserRole role)
    {
        var user = new User(Guid.NewGuid(), username, username.ToUpperInvariant(), "pending", role);
        user.ChangePinHash(new PasswordHasher<User>().HashPassword(user, pin));
        return user;
    }

    private ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}