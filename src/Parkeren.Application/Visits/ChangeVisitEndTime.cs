namespace Parkeren.Application.Visits;

public sealed record ChangeVisitEndTimeCommand(
    Guid OperationId,
    Guid VisitId,
    Guid ActorUserId,
    DateTimeOffset? DesiredEndAt);

public sealed record ChangeVisitEndTimeResult(
    Parkeren.Domain.Visits.Visit Visit,
    Parkeren.Domain.Visits.VisitEndTimeChange Change,
    bool IsReplay);

public interface IVisitEndTimeChanger
{
    Task<ChangeVisitEndTimeResult> ApplyAsync(
        ChangeVisitEndTimeCommand command,
        CancellationToken cancellationToken = default);
}
