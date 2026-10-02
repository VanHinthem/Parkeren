using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class VisitSchedulerWorkTests
{
    [Fact]
    public void Defer_moves_pending_work_to_later_due_time()
    {
        var dueAt = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VisitSchedulerWorkType.ContinueProviderCoverage,
            dueAt);

        var deferredUntil = dueAt.AddMinutes(1);
        work.Defer(deferredUntil);

        Assert.Equal(VisitSchedulerWorkStatus.Pending, work.Status);
        Assert.Equal(deferredUntil, work.DueAt);
        Assert.Null(work.ClaimedAt);
        Assert.Null(work.ClaimedBy);
    }

    [Fact]
    public void Defer_rejects_non_pending_work()
    {
        var dueAt = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VisitSchedulerWorkType.ContinueProviderCoverage,
            dueAt);
        work.Claim("worker-1", dueAt);

        Assert.Throws<InvalidOperationException>(() => work.Defer(dueAt.AddMinutes(1)));
    }

    [Fact]
    public void Defer_requires_later_due_time()
    {
        var dueAt = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VisitSchedulerWorkType.ContinueProviderCoverage,
            dueAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => work.Defer(dueAt));
    }
}
