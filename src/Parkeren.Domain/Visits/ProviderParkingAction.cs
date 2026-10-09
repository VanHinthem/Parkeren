namespace Parkeren.Domain.Visits;

public enum ProviderActionState { Planned, Starting, Scheduled, Active, Stopping, Stopped, Completed, Failed }
public enum ProviderActionHealth { Healthy, Unknown, Reconciling }
public enum ProviderHistoryStatus { NotRequired, Pending, Reconciled, Incomplete }

public sealed class ProviderParkingAction
{
    private ProviderParkingAction() { }
    public ProviderParkingAction(
        Guid id,
        Guid? visitId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        string? providerProductId = null,
        string? providerLocation = null,
        Guid? vehicleId = null)
    {
        if (vehicleId == Guid.Empty) throw new ArgumentException("Vehicle id must be non-empty.", nameof(vehicleId));
        if (plannedEndAt <= plannedStartAt) throw new ArgumentOutOfRangeException(nameof(plannedEndAt));
        Id = id; VisitId = visitId; PlannedStartAt = plannedStartAt; PlannedEndAt = plannedEndAt;
        ProviderProductId = providerProductId;
        ProviderLocation = providerLocation;
        VehicleId = vehicleId;
        State = ProviderActionState.Planned; Health = ProviderActionHealth.Healthy; CreatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid? VisitId { get; private set; }
    public ProviderActionOrigin Origin { get; private set; } = ProviderActionOrigin.Managed;
    public Guid? VehicleId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public ProviderActionAssignmentSource AssignmentSource { get; private set; } = ProviderActionAssignmentSource.Unassigned;
    public DateTimeOffset? FirstObservedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? ProviderActionId { get; private set; }
    public string? ProviderProductId { get; private set; }
    public string? ProviderLocation { get; private set; }
    public DateTimeOffset PlannedStartAt { get; private set; }
    public DateTimeOffset PlannedEndAt { get; private set; }
    public DateTimeOffset? ActualStartAt { get; private set; }
    public DateTimeOffset? ActualEndAt { get; private set; }
    public decimal? ProviderCostAmount { get; private set; }
    public ProviderHistoryStatus HistoryStatus { get; private set; } = ProviderHistoryStatus.NotRequired;
    public string? ProviderStatus { get; private set; }
    public ProviderActionState State { get; private set; }
    public ProviderActionHealth Health { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }
    /// <summary>
    /// Creates a completed historical action without issuing a provider mutation or creating a Visit.
    /// </summary>
    public static ProviderParkingAction ImportCompleted(
        Guid id,
        string providerActionId,
        string providerProductId,
        string? providerLocation,
        Guid? vehicleId,
        ProviderActionAssignment assignment,
        DateTimeOffset actualStartAt,
        DateTimeOffset actualEndAt,
        decimal? providerCostAmount,
        string providerStatus,
        DateTimeOffset observedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("An action id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(providerActionId))
            throw new ArgumentException("A provider action id is required.", nameof(providerActionId));
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("A provider product id is required.", nameof(providerProductId));
        if (string.IsNullOrWhiteSpace(providerStatus))
            throw new ArgumentException("A provider status is required.", nameof(providerStatus));
        if (actualEndAt < actualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        if (providerCostAmount is < 0m)
            throw new ArgumentOutOfRangeException(nameof(providerCostAmount));

        // Historical start and end are authoritative; no Start/Stop lifecycle transitions occur.
        var plannedEnd = actualEndAt > actualStartAt ? actualEndAt : actualStartAt.AddTicks(1);
        var action = new ProviderParkingAction(
            id, null, actualStartAt, plannedEnd, providerProductId, providerLocation);
        action.SetImportedAttribution(ProviderActionOrigin.Imported, vehicleId, assignment, observedAt);
        action.ProviderActionId = providerActionId.Trim();
        action.ProviderStatus = providerStatus.Trim();
        action.ActualStartAt = actualStartAt;
        action.ActualEndAt = actualEndAt;
        action.ProviderCostAmount = providerCostAmount;
        action.State = ProviderActionState.Completed;
        action.HistoryStatus = providerCostAmount.HasValue
            ? ProviderHistoryStatus.Reconciled
            : ProviderHistoryStatus.Incomplete;
        return action;
    }

    /// <summary>
    /// Refreshes authoritative facts for a completed action imported from provider history.
    /// Never changes vehicle, user attribution, Visit or managed action lifecycle.
    /// </summary>
    public void RefreshImportedHistory(
        DateTimeOffset actualStartAt,
        DateTimeOffset actualEndAt,
        decimal? providerCostAmount,
        string providerStatus,
        DateTimeOffset observedAt)
    {
        if (Origin is not (ProviderActionOrigin.Imported or ProviderActionOrigin.External)
            || VisitId is not null
            || State != ProviderActionState.Completed)
            throw new InvalidOperationException("Only completed external history actions without a Visit can be refreshed.");
        if (Health != ProviderActionHealth.Healthy)
            throw new InvalidOperationException("An unhealthy provider action cannot be refreshed.");
        if (actualEndAt < actualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        if (providerCostAmount is < 0m)
            throw new ArgumentOutOfRangeException(nameof(providerCostAmount));
        if (string.IsNullOrWhiteSpace(providerStatus))
            throw new ArgumentException("Provider status is required.", nameof(providerStatus));
        if (LastSyncedAt.HasValue && observedAt < LastSyncedAt.Value)
            throw new ArgumentOutOfRangeException(nameof(observedAt), "Sync observation cannot predate the last sync.");

        ActualStartAt = actualStartAt;
        ActualEndAt = actualEndAt;
        ProviderCostAmount = providerCostAmount;
        ProviderStatus = providerStatus.Trim();
        HistoryStatus = providerCostAmount.HasValue
            ? ProviderHistoryStatus.Reconciled
            : ProviderHistoryStatus.Incomplete;
        LastSyncedAt = observedAt;
    }

    /// <summary>
    /// Refreshes finalized provider facts for an already completed managed action.
    /// This is intentionally separate from the initial pending-history reconciliation path.
    /// </summary>
    public void RefreshFinalizedManagedHistory(
        DateTimeOffset actualStartAt,
        DateTimeOffset actualEndAt,
        decimal? providerCostAmount,
        string providerStatus,
        DateTimeOffset observedAt)
    {
        if (Origin != ProviderActionOrigin.Managed || VisitId is null || State != ProviderActionState.Completed)
            throw new InvalidOperationException("Only completed managed provider actions can be refreshed from finalized history.");
        if (Health != ProviderActionHealth.Healthy)
            throw new InvalidOperationException("An unhealthy managed provider action cannot be refreshed from history.");
        if (HistoryStatus is not (ProviderHistoryStatus.Reconciled or ProviderHistoryStatus.Incomplete))
            throw new InvalidOperationException("Managed history can only be refreshed after initial reconciliation is terminal.");
        if (string.IsNullOrWhiteSpace(ProviderActionId))
            throw new InvalidOperationException("A managed provider action must have a provider action id before history can be refreshed.");
        if (actualEndAt < actualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        if (providerCostAmount is < 0m)
            throw new ArgumentOutOfRangeException(nameof(providerCostAmount));
        if (string.IsNullOrWhiteSpace(providerStatus))
            throw new ArgumentException("Provider status is required.", nameof(providerStatus));
        if (LastSyncedAt.HasValue && observedAt < LastSyncedAt.Value)
            throw new ArgumentOutOfRangeException(nameof(observedAt), "Sync observation cannot predate the last sync.");

        ActualStartAt = actualStartAt;
        ActualEndAt = actualEndAt;
        if (providerCostAmount.HasValue)
            ProviderCostAmount = providerCostAmount.Value;
        ProviderStatus = providerStatus.Trim();
        HistoryStatus = providerCostAmount.HasValue
            ? ProviderHistoryStatus.Reconciled
            : ProviderHistoryStatus.Incomplete;
        if (FirstObservedAt is null)
            FirstObservedAt = observedAt;
        LastSyncedAt = observedAt;
    }

    public void SetImportedAttribution(
        ProviderActionOrigin origin,
        Guid? vehicleId,
        ProviderActionAssignment assignment,
        DateTimeOffset observedAt)
    {
        if (origin == ProviderActionOrigin.Managed)
            throw new ArgumentException("Import attribution requires an imported or external origin.", nameof(origin));
        if (Origin != ProviderActionOrigin.Managed || FirstObservedAt.HasValue || LastSyncedAt.HasValue)
            throw new InvalidOperationException("Import attribution is already initialized.");
        if (VisitId.HasValue || State != ProviderActionState.Planned || ProviderActionId is not null)
            throw new InvalidOperationException("Import attribution can only be initialized on an unstarted action without a Visit.");
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("Vehicle id must be non-empty when supplied.", nameof(vehicleId));
        if (vehicleId is null && assignment.Source != ProviderActionAssignmentSource.Unassigned)
            throw new ArgumentException("History without a vehicle must initially be unassigned.", nameof(assignment));
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Source == ProviderActionAssignmentSource.Confirmed)
            throw new ArgumentException("Imported actions cannot have confirmed app attribution.", nameof(assignment));

        Origin = origin;
        VehicleId = vehicleId;
        AssignedUserId = assignment.UserId;
        AssignmentSource = assignment.Source;
        FirstObservedAt = observedAt;
        LastSyncedAt = observedAt;
    }

    public void AssignHistoricalUser(ProviderActionAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (Origin == ProviderActionOrigin.Managed)
            throw new InvalidOperationException("Managed action attribution comes from its Visit.");
        if (assignment.Source is not (ProviderActionAssignmentSource.ManuallyAssigned or ProviderActionAssignmentSource.Unassigned))
            throw new ArgumentException("Only explicit manual assignment or unassignment is allowed.", nameof(assignment));
        if ((assignment.UserId is null) != (assignment.Source == ProviderActionAssignmentSource.Unassigned))
            throw new ArgumentException("Assignment and user must be consistent.", nameof(assignment));

        AssignedUserId = assignment.UserId;
        AssignmentSource = assignment.Source;
    }

    public void RecordHistorySync(DateTimeOffset observedAt)
    {
        if (Origin == ProviderActionOrigin.Managed && FirstObservedAt is null)
            FirstObservedAt = observedAt;
        LastSyncedAt = observedAt;
    }

    public void MarkStarting() { Ensure(ProviderActionState.Planned); State = ProviderActionState.Starting; }
    public void CaptureStartResponse(string providerActionId, DateTimeOffset actualStartAt, string? providerStatus = null) { Ensure(ProviderActionState.Starting); if (string.IsNullOrWhiteSpace(providerActionId)) throw new ArgumentException("Provider action id is required.", nameof(providerActionId)); ProviderActionId = providerActionId; ActualStartAt = actualStartAt; ProviderStatus = providerStatus; }
    public void MarkActive(string providerActionId, DateTimeOffset actualStartAt, string? providerStatus = null) { Ensure(ProviderActionState.Starting); CaptureStartResponse(providerActionId, actualStartAt, providerStatus); State = ProviderActionState.Active; Health = ProviderActionHealth.Healthy; }
    public void MarkScheduled(string providerActionId, string? providerStatus = null)
    {
        Ensure(ProviderActionState.Starting);
        if (string.IsNullOrWhiteSpace(providerActionId)) throw new ArgumentException("Provider action id is required.", nameof(providerActionId));
        ProviderActionId = providerActionId;
        ProviderStatus = providerStatus;
        ActualStartAt = null;
        State = ProviderActionState.Scheduled;
        Health = ProviderActionHealth.Healthy;
    }
    public void ActivateScheduled(DateTimeOffset actualStartAt, string? providerStatus = null)
    {
        Ensure(ProviderActionState.Scheduled);
        ActualStartAt = actualStartAt;
        ProviderStatus = providerStatus;
        State = ProviderActionState.Active;
        Health = ProviderActionHealth.Healthy;
    }
    public void ExtendPlannedEnd(DateTimeOffset newEndAt)
    {
        Ensure(ProviderActionState.Active);
        if (newEndAt <= PlannedEndAt)
            throw new ArgumentOutOfRangeException(nameof(newEndAt), "Extended provider action end must be later than the current planned end.");

        PlannedEndAt = newEndAt;
    }

    public void BeginStopping()
    {
        if (State is not (ProviderActionState.Active or ProviderActionState.Scheduled))
            throw new InvalidOperationException($"Only an active or scheduled provider action can be stopped, but was {State}.");
        if (string.IsNullOrWhiteSpace(ProviderActionId))
            throw new InvalidOperationException("A provider action must have a provider action id before it can be stopped.");
        State = ProviderActionState.Stopping;
    }

    public void MarkCompleted(DateTimeOffset endedAt)
    {
        Ensure(ProviderActionState.Active);
        if (ActualStartAt is DateTimeOffset startedAt && endedAt < startedAt)
            throw new ArgumentOutOfRangeException(nameof(endedAt));
        ActualEndAt = endedAt;
        State = ProviderActionState.Completed;
        Health = ProviderActionHealth.Healthy;
    }

    public void SetInitialProviderCost(decimal amount)
    {
        if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (State is not (ProviderActionState.Stopped or ProviderActionState.Completed))
            throw new InvalidOperationException("Initial provider cost requires a terminated provider action.");
        if (HistoryStatus != ProviderHistoryStatus.NotRequired)
            throw new InvalidOperationException("Initial provider cost can only be set before history reconciliation.");

        ProviderCostAmount = amount;
    }

    public void ScheduleHistoryReconciliation()
    {
        if (State is not (ProviderActionState.Stopped or ProviderActionState.Completed))
            throw new InvalidOperationException("History reconciliation requires a terminated provider action.");
        if (HistoryStatus == ProviderHistoryStatus.Pending) return;
        if (HistoryStatus != ProviderHistoryStatus.NotRequired)
            throw new InvalidOperationException("History reconciliation is already terminal.");

        HistoryStatus = ProviderHistoryStatus.Pending;
    }

    public void ApplyProviderHistory(
        DateTimeOffset actualStartAt,
        DateTimeOffset actualEndAt,
        decimal? providerCostAmount)
    {
        if (HistoryStatus != ProviderHistoryStatus.Pending)
            throw new InvalidOperationException("Provider history can only be applied to a pending reconciliation.");
        if (actualEndAt < actualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        if (providerCostAmount.HasValue && providerCostAmount.Value < 0m)
            throw new ArgumentOutOfRangeException(nameof(providerCostAmount));

        ActualStartAt = actualStartAt;
        ActualEndAt = actualEndAt;
        if (providerCostAmount.HasValue)
            ProviderCostAmount = providerCostAmount.Value;
        HistoryStatus = providerCostAmount.HasValue
            ? ProviderHistoryStatus.Reconciled
            : ProviderHistoryStatus.Incomplete;
    }

    public void MarkHistoryIncomplete()
    {
        if (HistoryStatus != ProviderHistoryStatus.Pending)
            throw new InvalidOperationException("Only pending history reconciliation can become incomplete.");

        HistoryStatus = ProviderHistoryStatus.Incomplete;
    }

    public void MarkExternallyStopped(string providerStatus)
    {
        Ensure(ProviderActionState.Active);
        if (string.IsNullOrWhiteSpace(providerStatus))
            throw new ArgumentException("Provider status is required.", nameof(providerStatus));
        ProviderStatus = providerStatus;
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
        // The provider did not supply an actual stop instant; observation time is not that instant.
    }

    public void MarkProviderMissing()
    {
        Ensure(ProviderActionState.Stopping);
        ProviderStatus = "missing";
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
        // Provider absence proves there is no action left to stop, but not when it ended.
    }

    public void MarkStopped(
        DateTimeOffset actualEndAt,
        string? providerStatus = null,
        DateTimeOffset? providerStartedAt = null)
    {
        Ensure(ProviderActionState.Stopping);
        var actualStartAt = ActualStartAt;
        if (!actualStartAt.HasValue && actualEndAt >= PlannedStartAt &&
            providerStartedAt is DateTimeOffset readBackStartAt && readBackStartAt <= actualEndAt)
        {
            actualStartAt = readBackStartAt;
        }
        if (actualStartAt is not null && actualEndAt < actualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        ActualStartAt = actualStartAt;
        ActualEndAt = actualEndAt;
        ProviderStatus = providerStatus;
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
    }

    public void MarkFailed() { if (State != ProviderActionState.Starting) throw new InvalidOperationException("Only a starting provider action can fail."); State = ProviderActionState.Failed; Health = ProviderActionHealth.Healthy; }
    public void MarkUnknown() { if (State is not ProviderActionState.Starting and not ProviderActionState.Stopping) throw new InvalidOperationException("Only an in-flight provider mutation can become unknown."); Health = ProviderActionHealth.Unknown; }
    public void BeginReconciliation() { if (Health != ProviderActionHealth.Unknown) throw new InvalidOperationException("Only an unknown provider action can be reconciled."); Health = ProviderActionHealth.Reconciling; }
    public void ResumeUnknownAfterInterruptedReconciliation() { if (Health != ProviderActionHealth.Reconciling) throw new InvalidOperationException("Only a reconciling provider action can resume as Unknown after restart."); Health = ProviderActionHealth.Unknown; }
    public void ResetForRetry() { if (State != ProviderActionState.Starting || Health != ProviderActionHealth.Unknown) throw new InvalidOperationException("Only an unknown starting provider action can be retried after reconciliation established no provider action exists."); if (!string.IsNullOrWhiteSpace(ProviderActionId)) throw new InvalidOperationException("A provider action with a captured provider id cannot be reset for retry."); State = ProviderActionState.Planned; Health = ProviderActionHealth.Healthy; ActualStartAt = null; ProviderStatus = null; }
    private void Ensure(ProviderActionState expected) { if (State != expected) throw new InvalidOperationException($"Expected provider action state {expected}, but was {State}."); }
}
