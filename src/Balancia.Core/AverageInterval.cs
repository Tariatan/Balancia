namespace Balancia.Core;

/// <summary>The calendar unit an average is taken per.</summary>
public enum AverageInterval
{
    Day,
    Week,
    Month,
    Year,
}

public static class AverageIntervals
{
    /// <summary>The number of calendar units touched by the inclusive range, counting units with no activity (weeks run Monday to Sunday).</summary>
    public static long CountPeriods(this AverageInterval interval, DateOnly from, DateOnly to) => interval switch
    {
        AverageInterval.Day => (long)to.DayNumber - from.DayNumber + 1,
        AverageInterval.Week => ((long)CalendarWeek.StartOfWeek(to).DayNumber - CalendarWeek.StartOfWeek(from).DayNumber) / 7 + 1,
        AverageInterval.Month => ((long)to.Year - from.Year) * 12 + to.Month - from.Month + 1,
        AverageInterval.Year => (long)to.Year - from.Year + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(interval)),
    };
}
