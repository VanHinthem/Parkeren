namespace Parkeren.Infrastructure.Visits;

internal static class VisitAdvisoryLock
{
    private const long VisitLockNamespace = 0x56495349; // VISI

    public static long For(Guid visitId)
    {
        if (visitId == Guid.Empty)
            throw new ArgumentException("Visit id is required.", nameof(visitId));

        return VisitLockNamespace ^ visitId.GetHashCode();
    }
}
