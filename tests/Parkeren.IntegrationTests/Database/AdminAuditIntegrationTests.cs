using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminAuditIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task User_policy_change_writes_actor_target_and_context()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"audit-admin-{suffix}", $"AUDIT-ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"audit-visitor-{suffix}", $"AUDIT-VISITOR-{suffix}", "hash", UserRole.Visitor);
        var defaults = new DefaultParkingPolicy(
            Guid.NewGuid(),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            allowVisitExtension: true,
            allowOpenEndedVisits: false,
            maxConcurrentVisits: 1);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor);
            seed.DefaultParkingPolicies.Add(defaults);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var provider = CreateServices().BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

            var result = await administration.SetUserPolicyAsync(
                admin.Id,
                visitor.Id,
                PolicyDurationOverrideMode.Unlimited,
                null,
                PolicyDurationOverrideMode.Unlimited,
                null,
                allowVisitExtension: null,
                allowOpenEndedVisits: null,
                maxConcurrentVisits: null,
                ct);

            Assert.Equal(AdminUserPolicyUpdateOutcome.Updated, result.Outcome);

            await using var verify = fixture.CreateDbContext();
            var audit = await verify.AdminAuditEvents.AsNoTracking()
                .SingleAsync(x => x.ActorUserId == admin.Id && x.Action == "UserPolicyChanged", ct);

            Assert.Equal(admin.Id, audit.ActorUserId);
            Assert.Equal("User", audit.TargetType);
            Assert.Equal(visitor.Id.ToString(), audit.TargetId);
            Assert.NotNull(audit.ContextJson);
            Assert.Contains($"\"username\":\"{visitor.Username}\"", audit.ContextJson);
            Assert.Contains("\"maxPaidParkingDurationMode\":\"Unlimited\"", audit.ContextJson);
            Assert.Contains("\"maxVisitElapsedDurationMode\":\"Unlimited\"", audit.ContextJson);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents
                .Where(x => x.ActorUserId == admin.Id || x.ActorUserId == visitor.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.UserPolicyOverrides.Where(x => x.UserId == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.DefaultParkingPolicies.Where(x => x.Id == defaults.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Pin_reset_audit_does_not_contain_pin_or_hash()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"pin-admin-{suffix}", $"PIN-ADMIN-{suffix}", "admin-hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"pin-visitor-{suffix}", $"PIN-VISITOR-{suffix}", "visitor-hash", UserRole.Visitor);
        const string newPin = "654321";

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var provider = CreateServices().BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            Assert.True(await authentication.ResetPinAsync(admin.Id, visitor.Id, newPin, ct));

            await using var verify = fixture.CreateDbContext();
            var audit = await verify.AdminAuditEvents.AsNoTracking()
                .SingleAsync(x => x.ActorUserId == admin.Id && x.Action == "UserPinReset", ct);

            Assert.Equal("User", audit.TargetType);
            Assert.Equal(visitor.Id.ToString(), audit.TargetId);
            Assert.NotNull(audit.ContextJson);
            Assert.Contains($"\"username\":\"{visitor.Username}\"", audit.ContextJson);
            Assert.False(audit.ContextJson.Contains(newPin, StringComparison.Ordinal));
            Assert.False(audit.ContextJson.Contains("pin", StringComparison.OrdinalIgnoreCase));
            Assert.False(audit.ContextJson.Contains("hash", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents
                .Where(x => x.ActorUserId == admin.Id || x.ActorUserId == visitor.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.UserSessions
                .Where(x => x.UserId == admin.Id || x.UserId == visitor.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor.Id).ExecuteDeleteAsync(ct);
        }
    }

    private ServiceCollection CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }
}
