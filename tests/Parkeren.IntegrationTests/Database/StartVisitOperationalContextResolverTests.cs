using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class StartVisitOperationalContextResolverTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Resolver_normalizes_offset_timestamps_before_querying_postgresql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var startAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(2));
        var desiredEndAt = startAt.AddHours(1);
        var defaultPolicyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var ruleSetId = Guid.NewGuid();
        var product = new ParkingProviderProduct(
            productId,
            $"utc-resolver-{Guid.NewGuid():N}",
            "UTC resolver test product",
            "test",
            "Test",
            "LOC_TEST",
            startAt.ToUniversalTime());
        var ruleSet = new ParkingRuleSet(
            ruleSetId,
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());
        ruleSet.AssignProviderProduct(productId);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.DefaultParkingPolicies.Add(new DefaultParkingPolicy(
                defaultPolicyId,
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(8),
                allowVisitExtension: true,
                allowOpenEndedVisits: false));
            seed.ParkingProviderProducts.Add(product);
            seed.ParkingRuleSets.Add(ruleSet);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IStartVisitOperationalContextResolver>();

        try
        {
            var result = await resolver.ResolveForProductAsync(
                Guid.NewGuid(), productId, startAt, desiredEndAt, cancellationToken);

            Assert.NotNull(result);
            Assert.Equal(desiredEndAt.ToUniversalTime(), result.CoverageEvaluationEndAt);
            Assert.Equal(TimeSpan.Zero, result.CoverageEvaluationEndAt.Offset);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingRuleSets.Where(x => x.Id == ruleSetId).ExecuteDeleteAsync(cancellationToken);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == productId).ExecuteDeleteAsync(cancellationToken);
            await cleanup.DefaultParkingPolicies.Where(x => x.Id == defaultPolicyId).ExecuteDeleteAsync(cancellationToken);
        }
    }
}