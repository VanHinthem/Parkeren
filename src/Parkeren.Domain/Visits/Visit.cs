using Parkeren.Domain.Rules;

namespace Parkeren.Domain.Visits;

public sealed class Visit
{
    private Visit() { }

    public Visit(Guid id, Guid startOperationId, Guid userId, Guid vehicleId, Guid startedByUserId, DateTimeOffset startAt, DateTimeOffset? desiredEndAt, EffectiveParkingPolicySnapshot policySnapshot)
    {
        ArgumentNullException.ThrowIfNull(policySnapshot);
        if (desiredEndAt is not null && desiredEndAt <= startAt)
            throw new ArgumentOutOfRangeException(nameof(desiredEndAt), "Desired end must be after start.");

        Id = id;
        StartOperationId = startOperationId;
        UserId = userId;
        VehicleId = vehicleId;
        StartedByUserId = startedByUserId;
        StartAt = startAt;
        DesiredEndAt = desiredEndAt;
        PolicySnapshot = policySnapshot;
        Status = VisitStatus.Starting;
        Health = VisitHealth.Healthy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid StartOperationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid StartedByUserId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset? DesiredEndAt { get; private set; }
    public DateTimeOffset? ActualEndAt { get; private set; }
    public VisitStatus Status { get; private set; }
    public VisitHealth Health { get; private set; }
    public EffectiveParkingPolicySnapshot PolicySnapshot { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool OccupiesCapacity => Status is not VisitStatus.Completed and not VisitStatus.Cancelled;

    public void Activate()
    {
        EnsureStatus(VisitStatus.Starting);
        Status = VisitStatus.Active;
    }

    public void EnsureDesiredEndCanChange(DateTimeOffset? desiredEndAt)
    {
        EnsureStatus(VisitStatus.Active);
        if (desiredEndAt is null && !PolicySnapshot.AllowOpenEndedVisits)
            throw new InvalidOperationException("Visit policy does not allow manual stop.");
        if (desiredEndAt is not null && desiredEndAt <= StartAt)
            throw new ArgumentOutOfRangeException(nameof(desiredEndAt), "Desired end must be after start.");
    }

    public void ChangeDesiredEndAt(DateTimeOffset? desiredEndAt)
    {
        EnsureDesiredEndCanChange(desiredEndAt);
        DesiredEndAt = desiredEndAt;
    }

    public void BeginStopping()
    {
        if (Status is not VisitStatus.Starting and not VisitStatus.Active)
            throw new InvalidOperationException($"Cannot stop Visit in {Status} state.");
        Status = VisitStatus.Stopping;
    }

    public void Complete(DateTimeOffset actualEndAt)
    {
        EnsureStatus(VisitStatus.Stopping);
        if (actualEndAt < StartAt) throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        ActualEndAt = actualEndAt;
        Status = VisitStatus.Completed;
        Health = VisitHealth.Healthy;
    }

    public void Cancel()
    {
        EnsureStatus(VisitStatus.Starting);
        Status = VisitStatus.Cancelled;
    }

    public void SetHealth(VisitHealth health)
    {
        if (Status is VisitStatus.Completed or VisitStatus.Cancelled && health != VisitHealth.Healthy)
            throw new InvalidOperationException("Closed Visits cannot have unhealthy operational state.");
        Health = health;
    }

    private void EnsureStatus(VisitStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Expected Visit state {expected}, but was {Status}.");
    }
}
