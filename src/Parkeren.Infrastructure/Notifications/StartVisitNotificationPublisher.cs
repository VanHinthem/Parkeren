using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Notifications;

internal sealed class StartVisitNotificationPublisher(ParkerenDbContext dbContext) : IStartVisitNotificationPublisher
{
    public async Task PublishStartedAsync(Visit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (visit.Status != VisitStatus.Active)
            throw new InvalidOperationException("A VisitStarted notification event can only be recorded for an active Visit.");

        var exists = await dbContext.NotificationEvents
            .AnyAsync(x => x.Type == NotificationEventType.VisitStarted && x.AggregateId == visit.Id, cancellationToken);

        if (exists)
            return;

        dbContext.NotificationEvents.Add(
            new NotificationEvent(Guid.NewGuid(), NotificationEventType.VisitStarted, visit.Id, DateTimeOffset.UtcNow));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var duplicate = await dbContext.NotificationEvents
                .AnyAsync(x => x.Type == NotificationEventType.VisitStarted && x.AggregateId == visit.Id, cancellationToken);
            if (!duplicate)
                throw;
        }
    }
}
