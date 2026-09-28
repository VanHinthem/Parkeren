namespace Parkeren.Infrastructure.Visits;

internal static class ProviderCoverageSchedule
{
    internal static DateTimeOffset PrecheckAt(DateTimeOffset providerEndAt) =>
        providerEndAt.AddMinutes(-5);
}
