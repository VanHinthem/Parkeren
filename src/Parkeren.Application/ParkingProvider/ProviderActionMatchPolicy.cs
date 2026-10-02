namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderActionMatchCriteria(
    string? ProviderActionId,
    string? ProviderProductId,
    string? LicensePlate = null,
    IReadOnlyCollection<string>? AllowedStatuses = null,
    DateTimeOffset? ExpectedStart = null,
    DateTimeOffset? ExpectedEnd = null);

public static class ProviderActionMatchPolicy
{
    /// <summary>
    /// V1 engineering margin for provider timestamp read-back differences.
    /// This is not a measured 2Park SLA. Start and End intentionally use the
    /// same tolerance until live provider evidence justifies different values.
    /// </summary>
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(5);

    public static ProviderParkingAction? FindUniqueMatch(
        IEnumerable<ProviderParkingAction> actions,
        ProviderActionMatchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(criteria);

        var candidates = actions.Where(action => Matches(action, criteria)).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static bool Matches(
        ProviderParkingAction action,
        ProviderActionMatchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(criteria);

        if (!string.IsNullOrWhiteSpace(criteria.ProviderActionId) &&
            !string.Equals(action.ProviderActionId, criteria.ProviderActionId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(criteria.ProviderProductId) &&
            !string.Equals(action.ProductId, criteria.ProviderProductId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(criteria.LicensePlate) &&
            !LicensePlatesMatch(action.LicensePlate, criteria.LicensePlate))
            return false;

        if (criteria.AllowedStatuses is { Count: > 0 } &&
            !criteria.AllowedStatuses.Any(status =>
                string.Equals(action.Status, status, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (criteria.ExpectedStart is DateTimeOffset expectedStart &&
            !TimestampsMatch(action.Start, expectedStart))
            return false;

        if (criteria.ExpectedEnd is DateTimeOffset expectedEnd &&
            !TimestampsMatch(action.End, expectedEnd))
            return false;

        // Location is intentionally not part of provider identity. 2Park can
        // return a provider label that differs from the locally configured code.
        return true;
    }

    public static bool TimestampsMatch(DateTimeOffset left, DateTimeOffset right) =>
        (left - right).Duration() <= TimestampTolerance;

    public static bool LicensePlatesMatch(string left, string right) =>
        string.Equals(NormalizeLicensePlate(left), NormalizeLicensePlate(right), StringComparison.Ordinal);

    private static string NormalizeLicensePlate(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
