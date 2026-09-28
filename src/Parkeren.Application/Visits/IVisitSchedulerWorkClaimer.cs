using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IVisitSchedulerWorkClaimer
{
    Task<VisitSchedulerWork?> ClaimNextDueAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}


public interface IVisitSchedulerWorkProcessor
{
    Task ProcessAsync(
        VisitSchedulerWork work,
        CancellationToken cancellationToken = default);
}
