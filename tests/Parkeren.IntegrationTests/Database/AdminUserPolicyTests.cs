using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminUserPolicyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Unlimited_duration_override_roundtrips_through_persistence_and_admin_read_model()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"visitor-{suffix}", $"VISITOR-{suffix}", "hash", UserRole.Visitor);
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

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
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
        Assert.NotNull(result.Policy);
        Assert.Null(result.Policy.Effective.MaxPaidParkingDurationMinutes);
        Assert.Null(result.Policy.Effective.MaxVisitElapsedDurationMinutes);
        Assert.Equal(PolicyDurationOverrideMode.Unlimited, result.Policy.Overrides.MaxPaidParkingDurationMode);
        Assert.Equal(PolicyDurationOverrideMode.Unlimited, result.Policy.Overrides.MaxVisitElapsedDurationMode);

        await using var verify = fixture.CreateDbContext();
        var stored = await verify.UserPolicyOverrides.AsNoTracking()
            .SingleAsync(x => x.UserId == visitor.Id, ct);
        Assert.Equal(PolicyDurationOverrideMode.Unlimited, stored.MaxPaidParkingDurationMode);
        Assert.Null(stored.MaxPaidParkingDuration);
        Assert.Equal(PolicyDurationOverrideMode.Unlimited, stored.MaxVisitElapsedDurationMode);
        Assert.Null(stored.MaxVisitElapsedDuration);
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
