namespace Parkeren.Domain.Rules;

public sealed class PaidWindow
{
    public PaidWindow(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (end <= start) throw new ArgumentException("Paid window end must be after start.");
        Day = day; Start = start; End = end;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ParkingRuleSetId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
}

public sealed class ParkingCalendarException
{
    private ParkingCalendarException() { }
    public ParkingCalendarException(DateOnly date, bool isPaid) { Date = date; IsPaid = isPaid; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ParkingRuleSetId { get; private set; }
    public DateOnly Date { get; private set; }
    public bool IsPaid { get; private set; }
}

public enum ProviderCoverageContinuation
{
    ExtendAction,
    StartNewAction
}

public sealed class ParkingRuleSet
{
    private ParkingRuleSet() { }

    public ParkingRuleSet(Guid id, DateTimeOffset validFrom, DateTimeOffset? validUntil, TimeSpan maxProviderActionDuration, IReadOnlyCollection<PaidWindow> paidWindows, IReadOnlyCollection<ParkingCalendarException>? calendarExceptions = null, bool publicHolidaysAreFree = false, ProviderCoverageContinuation continuation = ProviderCoverageContinuation.StartNewAction)
    {
        if (validUntil.HasValue && validUntil.Value <= validFrom) throw new ArgumentException("ValidUntil must be after ValidFrom.");
        if (maxProviderActionDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxProviderActionDuration));
        if (!Enum.IsDefined(continuation)) throw new ArgumentOutOfRangeException(nameof(continuation));
        Id=id; ValidFrom=validFrom; ValidUntil=validUntil; MaxProviderActionDuration=maxProviderActionDuration; Continuation=continuation; _paidWindows.AddRange(paidWindows); _calendarExceptions.AddRange(calendarExceptions ?? Array.Empty<ParkingCalendarException>()); PublicHolidaysAreFree=publicHolidaysAreFree;
    }
    public Guid Id { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }
    public TimeSpan MaxProviderActionDuration { get; private set; }
    public ProviderCoverageContinuation Continuation { get; private set; }
    private readonly List<PaidWindow> _paidWindows = new();
    private readonly List<ParkingCalendarException> _calendarExceptions = new();
    public IReadOnlyCollection<PaidWindow> PaidWindows => _paidWindows;
    public IReadOnlyCollection<ParkingCalendarException> CalendarExceptions => _calendarExceptions;
    public bool PublicHolidaysAreFree { get; private set; }
}

public sealed record ParkingTimeSegment(DateTimeOffset Start, DateTimeOffset End, bool IsPaid);

public static class ParkingTimeSegmenter
{
    public const string BusinessTimeZoneId = "Europe/Amsterdam";

    public static IReadOnlyList<ParkingTimeSegment> Segment(DateTimeOffset start, DateTimeOffset end, ParkingRuleSet rules)
    {
        if (end <= start) throw new ArgumentException("End must be after start.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        var cuts = new SortedSet<DateTimeOffset> { start, end };
        var localStart = TimeZoneInfo.ConvertTime(start, zone).Date;
        var localEnd = TimeZoneInfo.ConvertTime(end, zone).Date;

        for (var date = localStart; date <= localEnd; date = date.AddDays(1))
        {
            AddBoundary(date, TimeOnly.MinValue);
            foreach (var window in rules.PaidWindows.Where(x => x.Day == date.DayOfWeek))
            { AddBoundary(date, window.Start); AddBoundary(date, window.End); }
        }

        var points = cuts.ToArray();
        var result = new List<ParkingTimeSegment>();
        for (var i=0; i<points.Length-1; i++)
        {
            var a=points[i]; var b=points[i+1];
            var mid=a + TimeSpan.FromTicks((b-a).Ticks/2);
            var local=TimeZoneInfo.ConvertTime(mid,zone);
            var calendarException = rules.CalendarExceptions.SingleOrDefault(x => x.Date == DateOnly.FromDateTime(local.Date));
            var localDate = DateOnly.FromDateTime(local.Date);
            var isPublicHoliday = DutchPublicHolidayCalendar.ForYear(localDate.Year).Contains(localDate);
            var paid = calendarException?.IsPaid
                ?? (rules.PublicHolidaysAreFree && isPublicHoliday
                    ? false
                    : rules.PaidWindows.Any(w => w.Day==local.DayOfWeek && local.TimeOfDay>=w.Start.ToTimeSpan() && local.TimeOfDay<w.End.ToTimeSpan()));
            if(result.Count>0 && result[^1].IsPaid==paid && result[^1].End==a) result[^1]=result[^1] with { End=b };
            else result.Add(new(a,b,paid));
        }
        return result;

        void AddBoundary(DateTime date, TimeOnly time)
        {
            var local=DateTime.SpecifyKind(date.Add(time.ToTimeSpan()),DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local)) local=local.AddHours(1);
            var instant=new DateTimeOffset(local,zone.GetUtcOffset(local)).ToUniversalTime();
            if(instant>start && instant<end) cuts.Add(instant);
        }
    }
}
