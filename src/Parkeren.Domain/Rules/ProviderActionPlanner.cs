namespace Parkeren.Domain.Rules;

public sealed record ProviderActionPeriod(
    DateTimeOffset Start,
    DateTimeOffset End);

public static class ProviderActionPlanner
{
    public static IReadOnlyList<ProviderActionPeriod> Plan(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan maxProviderActionDuration)
    {
        if (end <= start) throw new ArgumentException("End must be after start.", nameof(end));
        if (maxProviderActionDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxProviderActionDuration));

        var actions = new List<ProviderActionPeriod>();
        var cursor = start;

        while (cursor < end)
        {
            var actionEnd = cursor + maxProviderActionDuration;
            if (actionEnd > end) actionEnd = end;

            actions.Add(new ProviderActionPeriod(cursor, actionEnd));
            cursor = actionEnd;
        }

        return actions;
    }
}
