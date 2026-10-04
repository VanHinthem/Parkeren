using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminProviderStatusServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Last_successful_balance_survives_service_scope_and_is_returned_as_stale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var productId = Guid.NewGuid();
        var product = new ParkingProviderProduct(
            productId,
            $"status-product-{productId:N}",
            "Status product",
            null,
            null,
            "OSS_J",
            DateTimeOffset.UtcNow);
        product.SetDefault(true);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.ParkingProviderProducts.Add(product);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var retrievedAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, now.Offset);
        var provider = new FlakyBalanceProvider(
            new ProviderBalance(12.34m, ProviderBalanceUnit.Euro, retrievedAt));

        try
        {
            AdminProviderStatus fresh;
            await using (var firstHost = CreateServices(provider))
            await using (var scope = firstHost.CreateAsyncScope())
            {
                fresh = await scope.ServiceProvider
                    .GetRequiredService<IAdminProviderStatusService>()
                    .GetStatusAsync(cancellationToken);
            }

            Assert.False(fresh.BalanceIsStale);
            Assert.Equal(12.34m, fresh.Balance?.RemainingBalance);
            Assert.Equal(retrievedAt, fresh.LastSuccessfulBalanceAt);

            AdminProviderStatus stale;
            await using (var restartedHost = CreateServices(provider))
            await using (var nextScope = restartedHost.CreateAsyncScope())
            {
                stale = await nextScope.ServiceProvider
                    .GetRequiredService<IAdminProviderStatusService>()
                    .GetStatusAsync(cancellationToken);
            }

            Assert.True(stale.BalanceIsStale);
            Assert.Equal(12.34m, stale.Balance?.RemainingBalance);
            Assert.Equal(ProviderBalanceUnit.Euro, stale.Balance?.Unit);
            Assert.Equal(retrievedAt, stale.Balance?.RetrievedAt);
            Assert.Equal(retrievedAt, stale.LastSuccessfulBalanceAt);
            Assert.NotNull(stale.BalanceError);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingProviderProducts
                .Where(x => x.Id == productId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Older_concurrent_balance_refresh_cannot_overwrite_newer_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var productId = Guid.NewGuid();
        var product = new ParkingProviderProduct(
            productId,
            $"concurrent-status-product-{productId:N}",
            "Concurrent status product",
            null,
            null,
            "OSS_J",
            DateTimeOffset.UtcNow);
        product.SetDefault(true);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.ParkingProviderProducts.Add(product);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var newerAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, now.Offset);
        var olderAt = newerAt.AddMinutes(-1);
        var provider = new OutOfOrderBalanceProvider(
            new ProviderBalance(4.56m, ProviderBalanceUnit.Euro, olderAt),
            new ProviderBalance(12.34m, ProviderBalanceUnit.Euro, newerAt));
        await using var services = CreateServices(provider);

        try
        {
            await using var olderScope = services.CreateAsyncScope();
            await using var newerScope = services.CreateAsyncScope();
            var olderContext = olderScope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var newerContext = newerScope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            Assert.NotSame(olderContext, newerContext);

            var olderRefresh = olderScope.ServiceProvider
                .GetRequiredService<IAdminProviderStatusService>()
                .GetStatusAsync(cancellationToken);
            await provider.OlderRequestStarted.Task.WaitAsync(cancellationToken);

            var newerRefresh = newerScope.ServiceProvider
                .GetRequiredService<IAdminProviderStatusService>()
                .GetStatusAsync(cancellationToken);
            await provider.NewerRequestStarted.Task.WaitAsync(cancellationToken);
            provider.CompleteNewerRequest();
            await newerRefresh;

            provider.CompleteOlderRequest();
            await olderRefresh;

            await using var verify = fixture.CreateDbContext();
            var saved = await verify.ParkingProviderProducts
                .AsNoTracking()
                .SingleAsync(x => x.Id == productId, cancellationToken);
            Assert.Equal(12.34m, saved.LastSuccessfulBalance);
            Assert.Equal(nameof(ProviderBalanceUnit.Euro), saved.LastSuccessfulBalanceUnit);
            Assert.Equal(newerAt, saved.LastSuccessfulBalanceAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingProviderProducts
                .Where(x => x.Id == productId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private ServiceProvider CreateServices(IParkingProvider parkingProvider)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton(parkingProvider);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        return services.BuildServiceProvider();
    }

    private sealed class FlakyBalanceProvider(ProviderBalance balance) : IParkingProvider
    {
        private int balanceCalls;

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref balanceCalls) == 1
                ? Task.FromResult(balance)
                : Task.FromException<ProviderBalance>(new HttpRequestException("Provider unavailable."));

        public Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderParkingAction>>([]);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class OutOfOrderBalanceProvider(
        ProviderBalance olderBalance,
        ProviderBalance newerBalance) : IParkingProvider
    {
        private int balanceCalls;
        public TaskCompletionSource OlderRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NewerRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ProviderBalance> olderResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ProviderBalance> newerResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void CompleteOlderRequest() => olderResponse.TrySetResult(olderBalance);
        public void CompleteNewerRequest() => newerResponse.TrySetResult(newerBalance);

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref balanceCalls) == 1)
            {
                OlderRequestStarted.TrySetResult();
                return olderResponse.Task;
            }

            NewerRequestStarted.TrySetResult();
            return newerResponse.Task;
        }

        public Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderParkingAction>>([]);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}