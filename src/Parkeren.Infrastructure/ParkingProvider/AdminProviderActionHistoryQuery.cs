using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class AdminProviderActionHistoryQuery(ParkerenDbContext db)
    : IAdminProviderActionHistoryQuery
{
    public async Task<AdminProviderActionHistoryPage> GetAsync(
        AdminProviderActionHistoryFilter filter, CancellationToken cancellationToken)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(filter), "Page must be positive and page size between 1 and 100.");

        if (filter.From.HasValue && filter.Until.HasValue && filter.From >= filter.Until)
            throw new ArgumentException("From must be earlier than Until.", nameof(filter));

        var actions = db.ProviderParkingActions.AsNoTracking().AsQueryable();
        if (filter.From.HasValue)
            actions = actions.Where(x => (x.ActualStartAt ?? x.PlannedStartAt) >= filter.From.Value);
        if (filter.Until.HasValue)
            actions = actions.Where(x => (x.ActualStartAt ?? x.PlannedStartAt) < filter.Until.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProviderProductId))
            actions = actions.Where(x => x.ProviderProductId == filter.ProviderProductId);
        if (filter.State.HasValue)
            actions = actions.Where(x => x.State == filter.State.Value);
        if (filter.Origin.HasValue)
            actions = actions.Where(x => x.Origin == filter.Origin.Value);
        if (filter.AssignedUserId.HasValue)
            actions = actions.Where(x => x.Origin == Parkeren.Domain.Visits.ProviderActionOrigin.Managed
                ? x.VisitId.HasValue && db.Visits.Any(v => v.Id == x.VisitId.Value && v.UserId == filter.AssignedUserId.Value)
                : x.AssignedUserId == filter.AssignedUserId.Value);
        if (filter.HasOpenDiscrepancy.HasValue)
            actions = actions.Where(x => db.ProviderDiscrepancies.Any(d =>
                d.ProviderParkingActionId == x.Id &&
                d.Status == Parkeren.Domain.ParkingProvider.ProviderDiscrepancyStatus.Open)
                == filter.HasOpenDiscrepancy.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            actions = actions.Where(x =>
                (x.ProviderActionId != null && EF.Functions.ILike(x.ProviderActionId, "%" + search + "%")) ||
                (x.VehicleId.HasValue && db.Vehicles.Any(v =>
                    v.Id == x.VehicleId.Value && EF.Functions.ILike(v.NormalizedLicensePlate, "%" + search + "%"))) ||
                (x.VehicleId == null && x.VisitId.HasValue && db.Visits.Any(visit => visit.Id == x.VisitId.Value &&
                    db.Vehicles.Any(v => v.Id == visit.VehicleId && EF.Functions.ILike(v.NormalizedLicensePlate, "%" + search + "%")))));
        }

        var total = await actions.CountAsync(cancellationToken);
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        var ordered = filter.OldestFirst
            ? actions.OrderBy(x => x.ActualStartAt ?? x.PlannedStartAt).ThenBy(x => x.Id)
            : actions.OrderByDescending(x => x.ActualStartAt ?? x.PlannedStartAt).ThenByDescending(x => x.Id);
        List<AdminProviderActionHistoryRow> rows = offset >= total
            ? []
            : await ordered.Skip((int)offset).Take(filter.PageSize)
                .Select(x => new AdminProviderActionHistoryRow(
                    x.Id, x.ProviderActionId, x.VisitId, x.ProviderProductId,
                    db.Vehicles.Where(v => v.Id == (x.VehicleId ?? db.Visits.Where(visit => visit.Id == x.VisitId)
                        .Select(visit => (Guid?)visit.VehicleId).FirstOrDefault()))
                        .Select(v => v.NormalizedLicensePlate).FirstOrDefault(),
                    x.ActualStartAt, x.ActualEndAt, x.ProviderCostAmount,
                    x.State, x.Origin, x.AssignedUserId, x.AssignmentSource,
                    x.HistoryStatus,
                    db.Users.Where(u => u.Id == (x.Origin == Parkeren.Domain.Visits.ProviderActionOrigin.Managed
                        ? db.Visits.Where(v => v.Id == x.VisitId).Select(v => (Guid?)v.UserId).FirstOrDefault()
                        : x.AssignedUserId))
                        .Select(u => u.Username).FirstOrDefault()))
                .ToListAsync(cancellationToken);
        return new AdminProviderActionHistoryPage(rows, filter.Page, filter.PageSize, total);
    }
}
