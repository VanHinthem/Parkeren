namespace Parkeren.Domain.Visits;

public sealed class VisitSchedulerAuditEvent
{
    private VisitSchedulerAuditEvent() { }

    public VisitSchedulerAuditEvent(
        Guid id,
        Guid visitId,
        DateTimeOffset occurredAt,
        int eventOrder,
        string sourceType,
        Guid sourceId,
        string eventType,
        string groupKey,
        int? attemptNumber,
        string reasonCode,
        string? detailsJson,
        string eventKey)
    {
        if (id == Guid.Empty) throw new ArgumentException("Audit event id is required.", nameof(id));
        if (visitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(visitId));
        if (eventOrder < 1) throw new ArgumentOutOfRangeException(nameof(eventOrder));
        if (sourceId == Guid.Empty) throw new ArgumentException("Source id is required.", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("Source type is required.", nameof(sourceType));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(groupKey)) throw new ArgumentException("Group key is required.", nameof(groupKey));
        if (attemptNumber < 0) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("Reason code is required.", nameof(reasonCode));
        if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentException("Event key is required.", nameof(eventKey));

        Id = id;
        VisitId = visitId;
        OccurredAt = occurredAt;
        EventOrder = eventOrder;
        SourceType = sourceType.Trim();
        SourceId = sourceId;
        EventType = eventType.Trim();
        GroupKey = groupKey.Trim();
        AttemptNumber = attemptNumber;
        ReasonCode = reasonCode.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? null : detailsJson;
        EventKey = eventKey.Trim();
    }

    public Guid Id { get; private set; }
    public Guid VisitId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public int EventOrder { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string GroupKey { get; private set; } = string.Empty;
    public int? AttemptNumber { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? DetailsJson { get; private set; }
    public string EventKey { get; private set; } = string.Empty;
}