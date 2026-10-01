namespace Parkeren.Domain.Rules;

public static class DutchPublicHolidayCalendar
{
    public static IReadOnlySet<DateOnly> ForYear(int year)
    {
        var easter = EasterSunday(year);
        var kingsDay = new DateOnly(year, 4, 27);
        if (kingsDay.DayOfWeek == DayOfWeek.Sunday) kingsDay = kingsDay.AddDays(-1);

        return new HashSet<DateOnly>
        {
            new(year, 1, 1),
            easter.AddDays(-2),
            easter,
            easter.AddDays(1),
            kingsDay,
            new(year, 5, 5),
            easter.AddDays(39),
            easter.AddDays(49),
            easter.AddDays(50),
            new(year, 12, 25),
            new(year, 12, 26)
        };
    }

    private static DateOnly EasterSunday(int year)
    {
        var a=year%19; var b=year/100; var c=year%100; var d=b/4; var e=b%4;
        var f=(b+8)/25; var g=(b-f+1)/3; var h=(19*a+b-d-g+15)%30;
        var i=c/4; var k=c%4; var l=(32+2*e+2*i-h-k)%7;
        var m=(a+11*h+22*l)/451; var month=(h+l-7*m+114)/31;
        var day=(h+l-7*m+114)%31+1;
        return new DateOnly(year,month,day);
    }
}
