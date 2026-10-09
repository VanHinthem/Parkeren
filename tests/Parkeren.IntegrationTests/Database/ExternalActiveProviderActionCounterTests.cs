using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ExternalActiveProviderActionCounterTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Counts_distinct_open_active_external_actions_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var providerId = $"external-count-{suffix}";
        var product = new ParkingProviderProduct(
            Guid.NewGuid(), providerId, "Test product", "182", "Oss",
            "OSS_J", DateTimeOffset.UtcNow);
        var observed = DateTimeOffset.UtcNow.AddMinutes(-10);

        ProviderDiscrepancy Create(string name, string? actionId, string? status,
            DateTimeOffset? endedAt = null)
            => new(Guid.NewGuid(), $"{providerId}:{name}",
                ProviderDiscrepancyType.ExternalProviderAction,
                product.Id, observed, providerActionId: actionId,
                providerStatus: status, providerStartAt: observed,
                providerEndAt: endedAt);

        var first = Create("first", "external-A", "ACTIVE");
        var duplicate = Create("duplicate", "external-A", "active");
        var second = Create("second", "external-B", "ACTIVE");
        var stopped = Create("stopped", "external-C", "STOPPED");
        var ended = Create("ended", "external-D", "ACTIVE", observed.AddMinutes(5));
        var unknown = Create("unknown", null, "ACTIVE");
        var resolved = Create("resolved", "external-E", "ACTIVE");
        resolved.Resolve(observed.AddMinutes(1));
        var records = new[] { first, duplicate, second, stopped, ended, unknown, resolved };

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ProviderDiscrepancies.AddRange(records);
                await seed.SaveChangesAsync(ct);
            }

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(2, await new ExternalActiveProviderActionCounter(verify)
                .CountAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            var ids = records.Select(x => x.Id).ToArray();
            await cleanup.ProviderDiscrepancies.Where(x => ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(ct);
        }
    }
}
