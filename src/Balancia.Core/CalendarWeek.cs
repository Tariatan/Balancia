namespace Balancia.Core;

/// <summary>Calendar weeks running Monday through Sunday.</summary>
public static class CalendarWeek
{
    private const int DaysInWeek = 7;

    public static DateOnly StartOfWeek(DateOnly date)
    {
        // DayOfWeek counts from Sunday (0); shifting by 6 makes Monday 0 and Sunday 6.
        var daysSinceMonday = ((int)date.DayOfWeek + DaysInWeek - 1) % DaysInWeek;

        return date.AddDays(-daysSinceMonday);
    }

    public static DateOnly EndOfWeek(DateOnly date) => StartOfWeek(date).AddDays(DaysInWeek - 1);
}
