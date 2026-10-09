using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ExternalActiveProviderActionCounterTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Excludes_external_discrepancy_when_provider_action_is_already_managed()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var productId = $"external-overlap-{suffix}";
        var providerActionId = $"managed-{suffix}";
        var product = new ParkingProviderProduct(
            Guid.NewGuid(), productId, "Test product", "182", "Oss",
            "OSS_J", DateTimeOffset.UtcNow);
        var start = DateTimeOffset.UtcNow.AddMinutes(-15);
        var managed = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), null, start, start.AddHours(1), productId);
        managed.MarkStarting();
        managed.MarkActive(providerActionId, start, "ACTIVE");
        var discrepancy = new ProviderDiscrepancy(
            Guid.NewGuid(), $"external-managed-{suffix}",
            ProviderDiscrepancyType.ExternalProviderAction, product.Id, start,
            providerActionId: providerActionId, providerStatus: "ACTIVE",
            providerStartAt: start);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ProviderParkingActions.Add(managed);
                seed.ProviderDiscrepancies.Add(discrepancy);
                await seed.SaveChangesAsync(ct);
            }

            await using var db = fixture.CreateDbContext();
            Assert.Equal(0, await new ExternalActiveProviderActionCounter(db)
                .CountAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderDiscrepancies.Where(x => x.Id == discrepancy.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.Id == managed.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

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
