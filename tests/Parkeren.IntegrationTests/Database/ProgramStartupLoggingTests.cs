using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parkeren.Api;
using Parkeren.Application.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

public sealed class ProgramStartupLoggingTests
{
    private const string ExceptionCanary = "startup-product-sync-sensitive-canary";
    private const string WarningMessage = "Initial provider product synchronization failed. Products can be synchronized manually from administration.";

    [Fact]
    public async Task Initial_product_sync_failure_does_not_log_exception_object_or_details()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(loggerProvider));
        services.AddSingleton<IProviderProductCatalogService, ThrowingProviderProductCatalogService>();

        await using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Parkeren.Api.ProgramStartupTasks");

        await ProgramStartupTasks.SynchronizeProviderProductsAsync(serviceProvider, logger);

        var warning = Assert.Single(loggerProvider.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Message == WarningMessage);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain(loggerProvider.Entries, entry =>
            entry.Message.Contains(ExceptionCanary, StringComparison.Ordinal) ||
            entry.Exception?.ToString().Contains(ExceptionCanary, StringComparison.Ordinal) == true);
    }

    private sealed class ThrowingProviderProductCatalogService : IProviderProductCatalogService
    {
        public Task<IReadOnlyList<ProviderProductSummary>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProductSyncResult> SynchronizeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderProductSyncResult>(new InvalidOperationException(ExceptionCanary));

        public Task<ProviderProductSyncResult> SynchronizeForAdminAsync(
            Guid actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProductSummary?> GetDefaultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProviderProductSummary> ResolveDefaultForStartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetDefaultAsync(Guid productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetDefaultForAdminAsync(Guid actorUserId, Guid productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record CapturedEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<CapturedEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new CapturedEntry(logLevel, formatter(state, exception), exception));
        }
    }
}