using Xunit;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;

namespace Parkeren.Application.Tests;

public sealed class VisitRecoveryClassifierTests
{
    [Theory]
    [InlineData(VisitStatus.Starting, ProviderOperationType.Start, VisitRecoveryKind.ReconcileStart)]
    [InlineData(VisitStatus.Active, ProviderOperationType.Extend, VisitRecoveryKind.ReconcileExtend)]
    [InlineData(VisitStatus.Stopping, ProviderOperationType.Stop, VisitRecoveryKind.ReconcileStop)]
    public void Matching_unresolved_operation_is_classified_for_reconciliation(
        VisitStatus visitStatus,
        ProviderOperationType operationType,
        VisitRecoveryKind expected)
    {
        var visit = CreateVisit(visitStatus);
        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, operationType);
        var item = new VisitRecoveryItem(visit, [], [operation]);

        var result = VisitRecoveryClassifier.Classify(item);

        Assert.Equal(expected, result.Kind);
        Assert.Same(operation, result.Operation);
    }

    [Fact]
    public void Active_visit_without_unresolved_operation_rebuilds_scheduler()
    {
        var visit = CreateVisit(VisitStatus.Active);

        var result = VisitRecoveryClassifier.Classify(new VisitRecoveryItem(visit, [], []));

        Assert.Equal(VisitRecoveryKind.RebuildScheduler, result.Kind);
        Assert.Null(result.Operation);
    }

    [Fact]
    public void Multiple_unresolved_operations_are_ambiguous()
    {
        var visit = CreateVisit(VisitStatus.Active);
        var operations = new[]
        {
            new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Extend),
            new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop)
        };

        var result = VisitRecoveryClassifier.Classify(new VisitRecoveryItem(visit, [], operations));

        Assert.Equal(VisitRecoveryKind.Ambiguous, result.Kind);
    }

    [Theory]
    [InlineData(VisitStatus.Starting)]
    [InlineData(VisitStatus.Stopping)]
    public void Non_active_visit_without_unresolved_operation_is_ambiguous(VisitStatus status)
    {
        var result = VisitRecoveryClassifier.Classify(
            new VisitRecoveryItem(CreateVisit(status), [], []));

        Assert.Equal(VisitRecoveryKind.Ambiguous, result.Kind);
    }

    [Fact]
    public void Mismatched_operation_and_visit_state_is_ambiguous()
    {
        var visit = CreateVisit(VisitStatus.Active);
        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop);

        var result = VisitRecoveryClassifier.Classify(
            new VisitRecoveryItem(visit, [], [operation]));

        Assert.Equal(VisitRecoveryKind.Ambiguous, result.Kind);
    }

    private static Visit CreateVisit(VisitStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now.AddMinutes(-10),
            now.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(
                new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));

        if (status == VisitStatus.Active)
            visit.Activate();
        else if (status == VisitStatus.Stopping)
        {
            visit.Activate();
            visit.BeginStopping();
        }

        return visit;
    }
}
