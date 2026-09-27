namespace Parkeren.Application.Visits;

public enum ChangeVisitEndTimeRoute
{
    Change,
    Stop
}

public static class ChangeVisitEndTimeRouting
{
    public static ChangeVisitEndTimeRoute Resolve(
        DateTimeOffset? desiredEndAt,
        DateTimeOffset now)
    {
        return desiredEndAt is not null && desiredEndAt.Value <= now
            ? ChangeVisitEndTimeRoute.Stop
            : ChangeVisitEndTimeRoute.Change;
    }
}
