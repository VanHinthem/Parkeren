using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Api;

internal sealed class NotificationRetentionOptions
{
    public int RetentionDays { get; set; } = 90;
}

internal sealed class NotificationRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await DeleteExpiredNotificationsAsync(stoppingToken);

                if (deleted > 0)
                    logger.LogInformation(
                        "Deleted {Count} inbox notifications older than {RetentionDays} days.",
                        deleted,
                        options.Value.RetentionDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Notification retention cleanup failed.");
            }

            await Task.Delay(Interval, timeProvider, stoppingToken);
        }
    }

    internal async Task<int> DeleteExpiredNotificationsAsync(CancellationToken cancellationToken)
    {
        var retentionDays = options.Value.RetentionDays;
        if (retentionDays <= 0)
            throw new InvalidOperationException("Notifications:RetentionDays must be greater than zero.");

        var cutoff = timeProvider.GetUtcNow().AddDays(-retentionDays);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
        return await dbContext.Notifications
            .Where(x => x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
