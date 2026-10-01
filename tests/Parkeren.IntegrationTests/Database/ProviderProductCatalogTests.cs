using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderProductCatalogTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task First_sync_with_multiple_products_does_not_choose_default()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearProductsAsync(cancellationToken);

        var provider = new ProductCatalogProvider(
        [
            new ProviderProduct("product-a", "Product A", "LOC_A", "category", "Category"),
            new ProviderProduct("product-b", "Product B", "LOC_B", "category", "Category")
        ]);

        try
        {
            await using var catalog = CreateCatalog(provider);

            var result = await catalog.Service.SynchronizeAsync(cancellationToken);

            Assert.False(result.DefaultAutoSelected);
            Assert.Equal(2, result.Products.Count);
            Assert.DoesNotContain(result.Products, product => product.IsDefault);
            Assert.All(result.Products, product => Assert.True(product.IsAvailable));
        }
        finally
        {
            await ClearProductsAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Sync_does_not_replace_missing_default_product()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearProductsAsync(cancellationToken);

        var existingDefault = new ParkingProviderProduct(
            Guid.NewGuid(),
            "product-old",
            "Old product",
            "category",
            "Category",
            "LOC_OLD",
            DateTimeOffset.UtcNow);
        existingDefault.SetDefault(true);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.ParkingProviderProducts.Add(existingDefault);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var provider = new ProductCatalogProvider(
        [
            new ProviderProduct("product-new", "New product", "LOC_NEW", "category", "Category")
        ]);

        try
        {
            await using var catalog = CreateCatalog(provider);

            var result = await catalog.Service.SynchronizeAsync(cancellationToken);

            Assert.False(result.DefaultAutoSelected);

            var unavailableDefault = Assert.Single(
                result.Products,
                product => product.ProviderProductId == "product-old");
            Assert.False(unavailableDefault.IsAvailable);
            Assert.True(unavailableDefault.IsDefault);

            var availableProduct = Assert.Single(
                result.Products,
                product => product.ProviderProductId == "product-new");
            Assert.True(availableProduct.IsAvailable);
            Assert.False(availableProduct.IsDefault);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => catalog.Service.ResolveDefaultForStartAsync(cancellationToken));
        }
        finally
        {
            await ClearProductsAsync(cancellationToken);
        }
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
