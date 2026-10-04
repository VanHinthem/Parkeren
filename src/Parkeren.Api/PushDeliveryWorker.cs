using Parkeren.Infrastructure.Notifications;

namespace Parkeren.Api;

internal sealed class PushDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PushDeliveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<PushDeliveryProcessor>();
                var processed = await processor.ProcessNextAsync(stoppingToken);

                if (!processed)
                    await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Push delivery worker failed.");
                await Task.Delay(ErrorDelay, timeProvider, stoppingToken);
            }
        }
    }
}
