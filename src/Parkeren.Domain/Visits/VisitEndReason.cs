namespace Parkeren.Domain.Visits;

public enum VisitEndReason
{
    ManualStop,
    DesiredEndReached,
    MaxVisitElapsedDurationReached,
    MaxPaidParkingDurationReached
}
