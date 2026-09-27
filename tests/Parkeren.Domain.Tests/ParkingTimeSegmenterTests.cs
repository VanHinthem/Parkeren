using Xunit;
using Parkeren.Domain.Rules;
namespace Parkeren.Domain.Tests;
public sealed class ParkingTimeSegmenterTests
{
    private static ParkingRuleSet Rules()=>new(Guid.NewGuid(),DateTimeOffset.MinValue,null,TimeSpan.FromHours(4),Enumerable.Range(1,6).SelectMany(_=>Array.Empty<PaidWindow>()).Concat(new[]{DayOfWeek.Monday,DayOfWeek.Tuesday,DayOfWeek.Wednesday,DayOfWeek.Thursday,DayOfWeek.Friday,DayOfWeek.Saturday}.Select(d=>new PaidWindow(d,new TimeOnly(9,0),new TimeOnly(20,0)))).ToArray());
    [Fact] public void Overnight_visit_only_counts_next_morning_paid_time(){var start=new DateTimeOffset(2026,9,28,18,0,0,TimeSpan.Zero);var end=new DateTimeOffset(2026,9,29,8,0,0,TimeSpan.Zero);var s=ParkingTimeSegmenter.Segment(start,end,Rules());Assert.Equal(2,s.Count);Assert.False(s[0].IsPaid);Assert.True(s[1].IsPaid);Assert.Equal(TimeSpan.FromHours(1),s.Where(x=>x.IsPaid).Aggregate(TimeSpan.Zero,(a,x)=>a+(x.End-x.Start)));}
    [Fact] public void Sunday_is_free(){var start=new DateTimeOffset(2026,9,27,8,0,0,TimeSpan.Zero);var end=start.AddHours(8);Assert.All(ParkingTimeSegmenter.Segment(start,end,Rules()),x=>Assert.False(x.IsPaid));}
}
