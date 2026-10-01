using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderDiscrepancyPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Discrepancy_can_be_persisted_and_resolved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var product = new ParkingProviderProduct(
            Guid.NewGuid(),
            $"discrepancy-{suffix}",
            "Discrepancy test product",
            null,
            null,
            "TEST",
            now);
        var discrepancy = new ProviderDiscrepancy(
            Guid.NewGuid(),
            $"external-action:{suffix}",
            ProviderDiscrepancyType.ExternalProviderAction,
            product.Id,
            now,
            providerActionId: $"action-{suffix}",
            providerStatus: "active",
            providerStartAt: now,
            providerEndAt: now.AddHours(1));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ProviderDiscrepancies.Add(discrepancy);
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using (var update = fixture.CreateDbContext())
            {
                var persisted = await update.ProviderDiscrepancies
                    .SingleAsync(x => x.Id == discrepancy.Id, cancellationToken);

                Assert.Equal(ProviderDiscrepancyStatus.Open, persisted.Status);
                Assert.Equal(product.Id, persisted.ProviderProductId);
                Assert.Equal("active", persisted.ProviderStatus);

                persisted.Resolve(now.AddMinutes(5));
                await update.SaveChangesAsync(cancellationToken);
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var persisted = await verify.ProviderDiscrepancies
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == discrepancy.Id, cancellationToken);

                Assert.Equal(ProviderDiscrepancyStatus.Resolved, persisted.Status);
                Assert.Equal(now.AddMinutes(5), persisted.ResolvedAt);
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderDiscrepancies
                .Where(x => x.Id == discrepancy.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ParkingProviderProducts
                .Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
