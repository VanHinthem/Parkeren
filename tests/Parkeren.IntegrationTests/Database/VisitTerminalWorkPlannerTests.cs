using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Visits;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitTerminalWorkPlannerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Ensure_is_idempotent_for_matching_terminal_boundary()
    {
        var ct = TestContext.Current.CancellationToken;
        var visit = await SeedActiveVisitAsync(ct);
        await using var context = fixture.CreateDbContext();
        var trackedVisit = await context.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var planner = new VisitTerminalWorkPlanner(context);

        await planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct);
        await context.SaveChangesAsync(ct);
        await planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct);
        await context.SaveChangesAsync(ct);

        var work = await context.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id && x.Status == VisitSchedulerWorkStatus.Pending)
            .ToListAsync(ct);
        Assert.Single(work);
        Assert.Equal(VisitSchedulerWorkType.StopVisit, work[0].Type);
        Assert.Equal(VisitEndReason.DesiredEndReached, work[0].EndReason);
        Assert.Equal(visit.DesiredEndAt, work[0].DueAt);
    }

    [Fact]
    public async Task Ensure_replaces_obsolete_pending_terminal_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var visit = await SeedActiveVisitAsync(ct);
        await using var context = fixture.CreateDbContext();
        var trackedVisit = await context.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var planner = new VisitTerminalWorkPlanner(context);

        await planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct);
        await context.SaveChangesAsync(ct);
        var newEnd = trackedVisit.StartAt.AddHours(1);
        trackedVisit.ChangeDesiredEndAt(newEnd);
        await planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct);
        await context.SaveChangesAsync(ct);

        var work = await context.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        Assert.Equal(2, work.Count);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled, work[0].Status);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, work[1].Status);
        Assert.Equal(newEnd, work[1].DueAt);
        Assert.Equal(VisitEndReason.DesiredEndReached, work[1].EndReason);
    }

    [Fact]
    public async Task Ensure_does_not_replace_claimed_terminal_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var visit = await SeedActiveVisitAsync(ct);
        await using var context = fixture.CreateDbContext();
        var trackedVisit = await context.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var planner = new VisitTerminalWorkPlanner(context);

        await planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct);
        await context.SaveChangesAsync(ct);
        var work = await context.VisitSchedulerWork.SingleAsync(x => x.VisitId == visit.Id, ct);
        work.Claim("terminal-test", DateTimeOffset.UtcNow);
        trackedVisit.ChangeDesiredEndAt(trackedVisit.StartAt.AddHours(1));
        await context.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.EnsureAsync(trackedVisit, Array.Empty<ParkingRuleSet>(), ct));
    }

    private async Task<Visit> SeedActiveVisitAsync(CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var username = $"terminal-{userId:N}";
        var plate = $"T{vehicleId:N}"[..8];
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            vehicleId,
            userId,
            startAt,
            startAt.AddHours(2),
            EffectiveParkingPolicySnapshot.Capture(
                new EffectiveParkingPolicy(null, TimeSpan.FromHours(8), true)));
        visit.Activate();

        await using var context = fixture.CreateDbContext();
        context.Users.Add(new User(userId, username, username.ToUpperInvariant(), "hash", UserRole.Visitor));
        context.Vehicles.Add(new Vehicle(vehicleId, plate, plate, null));
        context.Visits.Add(visit);
        await context.SaveChangesAsync(cancellationToken);
        return visit;
    }
}
