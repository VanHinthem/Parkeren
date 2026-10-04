using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

public sealed class VisitTerminalWorkPlanner(ParkerenDbContext dbContext)
{
    public async Task<VisitTerminalBoundary?> EnsureAsync(
        Visit visit,
        IReadOnlyCollection<ParkingRuleSet> ruleSets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(ruleSets);

        if (visit.Status != VisitStatus.Active)
            throw new InvalidOperationException("Terminal scheduler work can only be ensured for an active Visit.");

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, ruleSets);
        var activeTerminalWork = await dbContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        x.Type == VisitSchedulerWorkType.StopVisit &&
                        (x.Status == VisitSchedulerWorkStatus.Pending ||
                         x.Status == VisitSchedulerWorkStatus.Claimed))
            .OrderBy(x => x.DueAt)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        if (boundary is not null)
        {
            var exact = activeTerminalWork.FirstOrDefault(x =>
                x.DueAt == boundary.At &&
                x.EndReason == boundary.Reason);

            if (exact is not null)
            {
                foreach (var duplicate in activeTerminalWork.Where(x =>
                             x.Id != exact.Id &&
                             x.Status == VisitSchedulerWorkStatus.Pending))
                {
                    duplicate.Cancel("duplicate_terminal_work");
                }

                return boundary;
            }
        }

        if (activeTerminalWork.Any(x => x.Status == VisitSchedulerWorkStatus.Claimed))
            throw new InvalidOperationException("Claimed terminal scheduler work cannot be replaced while it is being processed.");

        foreach (var obsolete in activeTerminalWork.Where(x => x.Status == VisitSchedulerWorkStatus.Pending))
            obsolete.Cancel("terminal_boundary_replanned");

        if (boundary is not null)
        {
            dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                Guid.NewGuid(),
                visit.Id,
                VisitSchedulerWorkType.StopVisit,
                boundary.At,
                boundary.Reason));
        }

        return boundary;
    }
}
