using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parkeren.Api;

namespace Parkeren.IntegrationTests.Workers;

public sealed class BackgroundWorkerLoggingTests
{
    [Fact]
    public async Task Push_delivery_worker_does_not_log_exception_objects()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var logger = new CapturingLogger<PushDeliveryWorker>();
        var worker = new PushDeliveryWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await logger.FirstEntry.Task.WaitAsync(TestContext.Current.CancellationToken);
            AssertSafeError(logger, "Push delivery worker failed.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Notification_retention_worker_does_not_log_exception_objects()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var logger = new CapturingLogger<NotificationRetentionWorker>();
        var worker = new NotificationRetentionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationRetentionOptions()),
            TimeProvider.System,
            logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await logger.FirstEntry.Task.WaitAsync(TestContext.Current.CancellationToken);
            AssertSafeError(logger, "Notification retention cleanup failed.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static void AssertSafeError<T>(CapturingLogger<T> logger, string message)
    {
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message == message);
        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public TaskCompletionSource FirstEntry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
            FirstEntry.TrySetResult();
        }
    }
}
