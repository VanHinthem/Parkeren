namespace Parkeren.Domain.Visits;

public enum VisitSchedulerWorkExecutionDecision
{
    Execute,
    Defer,
    Cancel
}

public static class VisitSchedulerWorkExecutionPolicy
{
    public static readonly TimeSpan DefaultDeferDelay = TimeSpan.FromMinutes(1);

    public static VisitSchedulerWorkExecutionDecision Evaluate(
        VisitSchedulerWorkType workType,
        VisitStatus visitStatus,
        VisitHealth visitHealth)
    {
        return workType switch
        {
            VisitSchedulerWorkType.ContinueProviderCoverage => EvaluateContinuation(visitStatus, visitHealth),
            VisitSchedulerWorkType.StopVisit => EvaluateStop(visitStatus),
            VisitSchedulerWorkType.LongVisitWarning => EvaluateLongVisitWarning(visitStatus),
            VisitSchedulerWorkType.ReconcileProviderAction => EvaluateProviderActionReconciliation(visitStatus),
            _ => throw new ArgumentOutOfRangeException(nameof(workType), workType, "Unsupported scheduler work type.")
        };
    }

    private static VisitSchedulerWorkExecutionDecision EvaluateProviderActionReconciliation(VisitStatus status)
    {
        return status switch
        {
            VisitStatus.Starting or VisitStatus.Active or VisitStatus.Stopping or VisitStatus.Completed or VisitStatus.Cancelled
                => VisitSchedulerWorkExecutionDecision.Execute,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Visit status.")
        };
    }

    private static VisitSchedulerWorkExecutionDecision EvaluateContinuation(
        VisitStatus status,
        VisitHealth health)
    {
        return status switch
        {
            VisitStatus.Starting => VisitSchedulerWorkExecutionDecision.Defer,
            VisitStatus.Active when health == VisitHealth.Healthy => VisitSchedulerWorkExecutionDecision.Execute,
            VisitStatus.Active => VisitSchedulerWorkExecutionDecision.Defer,
            VisitStatus.Stopping or VisitStatus.Completed or VisitStatus.Cancelled => VisitSchedulerWorkExecutionDecision.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Visit status.")
        };
    }

    private static VisitSchedulerWorkExecutionDecision EvaluateStop(VisitStatus status)
    {
        return status switch
        {
            VisitStatus.Starting or VisitStatus.Active or VisitStatus.Stopping => VisitSchedulerWorkExecutionDecision.Execute,
            VisitStatus.Completed or VisitStatus.Cancelled => VisitSchedulerWorkExecutionDecision.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Visit status.")
        };
    }

    private static VisitSchedulerWorkExecutionDecision EvaluateLongVisitWarning(VisitStatus status)
    {
        return status switch
        {
            VisitStatus.Starting => VisitSchedulerWorkExecutionDecision.Defer,
            VisitStatus.Active => VisitSchedulerWorkExecutionDecision.Execute,
            VisitStatus.Stopping or VisitStatus.Completed or VisitStatus.Cancelled => VisitSchedulerWorkExecutionDecision.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported Visit status.")
        };
    }
}
