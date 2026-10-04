using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
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

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(), startAt, desiredEndAt, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(desiredEndAt.ToUniversalTime(), result.CoverageEvaluationEndAt);
        Assert.Equal(TimeSpan.Zero, result.CoverageEvaluationEndAt.Offset);
    }
}