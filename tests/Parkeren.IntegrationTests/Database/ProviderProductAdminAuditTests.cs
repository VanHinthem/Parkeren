using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderProductAdminAuditTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Admin_sync_writes_provider_product_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearProductsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(
            Guid.NewGuid(),
            $"provider-audit-admin-{suffix}",
            $"PROVIDER-AUDIT-ADMIN-{suffix}",
            "hash",
            UserRole.Admin);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(admin);
            await seed.SaveChangesAsync(ct);
        }

        var parkingProvider = new ProductCatalogProvider(
        [
            new ProviderProduct("product-a", "Product A", "LOC_A", "category", "Category"),
            new ProviderProduct("product-b", "Product B", "LOC_B", "category", "Category")
        ]);

        try
        {
            await using var catalog = CreateCatalog(parkingProvider);
            var result = await catalog.Service.SynchronizeForAdminAsync(admin.Id, ct);

            Assert.Equal(2, result.Products.Count);

            await using var verify = fixture.CreateDbContext();
            var audit = await verify.AdminAuditEvents.AsNoTracking()
                .SingleAsync(
                    x => x.ActorUserId == admin.Id && x.Action == "ProviderProductsSynchronized",
                    ct);

            Assert.Equal("ParkingProviderProductCatalog", audit.TargetType);
            Assert.Null(audit.TargetId);
            Assert.NotNull(audit.ContextJson);

            using var context = JsonDocument.Parse(audit.ContextJson);
            Assert.Equal(2, context.RootElement.GetProperty("providerProductCount").GetInt32());
            Assert.Equal(2, context.RootElement.GetProperty("availableProductCount").GetInt32());
            Assert.False(context.RootElement.GetProperty("defaultAutoSelected").GetBoolean());
        }
        finally
        {
            await CleanupAsync(admin.Id, ct);
        }
    }

    [Fact]
    public async Task Repeating_same_admin_default_does_not_write_duplicate_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearProductsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(
            Guid.NewGuid(),
            $"provider-default-admin-{suffix}",
            $"PROVIDER-DEFAULT-ADMIN-{suffix}",
            "hash",
            UserRole.Admin);
        var seenAt = DateTimeOffset.UtcNow;
        var productA = new ParkingProviderProduct(
            Guid.NewGuid(),
            "product-a",
            "Product A",
            "category",
            "Category",
            "LOC_A",
            seenAt);
        productA.SetDefault(true);
        var productB = new ParkingProviderProduct(
            Guid.NewGuid(),
            "product-b",
            "Product B",
            "category",
            "Category",
            "LOC_B",
            seenAt);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(admin);
            seed.ParkingProviderProducts.AddRange(productA, productB);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var catalog = CreateCatalog(new ProductCatalogProvider(Array.Empty<ProviderProduct>()));

            Assert.True(await catalog.Service.SetDefaultForAdminAsync(admin.Id, productB.Id, ct));
            Assert.True(await catalog.Service.SetDefaultForAdminAsync(admin.Id, productB.Id, ct));

            await using var verify = fixture.CreateDbContext();
            var audits = await verify.AdminAuditEvents.AsNoTracking()
                .Where(
                    x => x.ActorUserId == admin.Id &&
                         x.Action == "ProviderProductDefaultChanged")
                .ToListAsync(ct);

            var audit = Assert.Single(audits);
            Assert.Equal("ParkingProviderProduct", audit.TargetType);
            Assert.Equal(productB.Id.ToString(), audit.TargetId);
            Assert.NotNull(audit.ContextJson);

            using var context = JsonDocument.Parse(audit.ContextJson);
            Assert.Equal(productA.Id, context.RootElement.GetProperty("previousDefaultProductId").GetGuid());
            Assert.Equal(productB.Id, context.RootElement.GetProperty("productId").GetGuid());
            Assert.Equal("product-b", context.RootElement.GetProperty("providerProductId").GetString());
        }
        finally
        {
            await CleanupAsync(admin.Id, ct);
        }
    }

    private async Task CleanupAsync(Guid adminId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.AdminAuditEvents
            .Where(x => x.ActorUserId == adminId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ProviderDiscrepancies.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingProviderProducts.ExecuteDeleteAsync(cancellationToken);
        await context.Users.Where(x => x.Id == adminId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ClearProductsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.ProviderDiscrepancies.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingProviderProducts.ExecuteDeleteAsync(cancellationToken);
    }

    private CatalogScope CreateCatalog(IParkingProvider parkingProvider)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        return new CatalogScope(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<IProviderProductCatalogService>());
    }

    private sealed class CatalogScope(
        ServiceProvider provider,
        AsyncServiceScope scope,
        IProviderProductCatalogService service) : IAsyncDisposable
    {
        public IProviderProductCatalogService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class ProductCatalogProvider(
        IReadOnlyList<ProviderProduct> products) : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderProduct>> GetProductsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(products);

        public Task<ProviderProduct> GetProductAsync(
            CancellationToken cancellationToken = default) =>
            products.Count == 1
                ? Task.FromResult(products[0])
                : throw new InvalidOperationException();

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderBalance> GetBalanceAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(
            string providerActionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
