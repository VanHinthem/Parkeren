namespace Parkeren.Domain.Rules;
public sealed record PaidWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End)
{
    public PaidWindow : this(Day, Start, End) { if (End <= Start) throw new ArgumentException("Paid window end must be after start."); }
}
public sealed class ParkingRuleSet
{
    public ParkingRuleSet(Guid id, DateTimeOffset validFrom, DateTimeOffset? validUntil, TimeSpan maxProviderActionDuration, IReadOnlyCollection<PaidWindow> paidWindows)
    { if(validUntil<=validFrom)throw new ArgumentException("ValidUntil must be after ValidFrom.");Id=id;ValidFrom=validFrom;ValidUntil=validUntil;MaxProviderActionDuration=maxProviderActionDuration;PaidWindows=paidWindows; }
    public Guid Id{get;} public DateTimeOffset ValidFrom{get;} public DateTimeOffset? ValidUntil{get;} public TimeSpan MaxProviderActionDuration{get;} public IReadOnlyCollection<PaidWindow> PaidWindows{get;}
}
public sealed record ParkingTimeSegment(DateTimeOffset Start,DateTimeOffset End,bool IsPaid);
public static class ParkingTimeSegmenter
{
    public const string BusinessTimeZoneId="Europe/Amsterdam";
    public static IReadOnlyList<ParkingTimeSegment> Segment(DateTimeOffset start,DateTimeOffset end,ParkingRuleSet rules)
    {
        if(end<=start)throw new ArgumentException("End must be after start.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);var cuts=new SortedSet<DateTimeOffset>{start,end};
        var localStart=TimeZoneInfo.ConvertTime(start,zone).Date;var localEnd=TimeZoneInfo.ConvertTime(end,zone).Date;
        for(var date=localStart;date<=localEnd;date=date.AddDays(1))
        foreach(var w in rules.PaidWindows.Where(x=>x.Day==date.DayOfWeek))
        { Add(date,w.Start);Add(date,w.End); }
        var points=cuts.ToArray();var result=new List<ParkingTimeSegment>();
        for(var i=0;i<points.Length-1;i++){var a=points[i];var b=points[i+1];if(b<=start||a>=end)continue;a=a<start?start:a;b=b>end?end:b;var mid=a+(b-a)/2;var local=TimeZoneInfo.ConvertTime(mid,zone);var paid=rules.PaidWindows.Any(w=>w.Day==local.DayOfWeek&&local.TimeOfDay>=w.Start.ToTimeSpan()&&local.TimeOfDay<w.End.ToTimeSpan());if(result.Count>0&&result[^1].IsPaid==paid&&result[^1].End==a)result[^1]=result[^1] with{End=b};else result.Add(new(a,b,paid));}
        return result;
        void Add(DateTime date,TimeOnly time){var local=DateTime.SpecifyKind(date.Add(time.ToTimeSpan()),DateTimeKind.Unspecified);if(zone.IsInvalidTime(local))local=local.AddHours(1);var offset=zone.GetUtcOffset(local);var instant=new DateTimeOffset(local,offset).ToUniversalTime();if(instant>start&&instant<end)cuts.Add(instant);}
    }
}
