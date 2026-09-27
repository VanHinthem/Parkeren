namespace Parkeren.Domain.Rules;

public static class ParkingRuleSetResolver
{
    public static ParkingRuleSet Resolve(
        IEnumerable<ParkingRuleSet> ruleSets,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);

        var applicable = ruleSets
            .Where(x => x.ValidFrom <= instant &&
                        (!x.ValidUntil.HasValue || instant < x.ValidUntil.Value))
            .ToArray();

        return applicable.Length switch
        {
            1 => applicable[0],
            0 => throw new InvalidOperationException($"No parking rule set applies at {instant:O}."),
            _ => throw new InvalidOperationException($"Multiple parking rule sets apply at {instant:O}.")
        };
    }

    public static void ValidateNoOverlap(IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);

        var ordered = ruleSets.OrderBy(x => x.ValidFrom).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            if (!previous.ValidUntil.HasValue || current.ValidFrom < previous.ValidUntil.Value)
                throw new InvalidOperationException("Parking rule sets may not overlap.");
        }
    }
}
