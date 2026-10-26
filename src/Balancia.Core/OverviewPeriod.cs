namespace Balancia.Core;

/// <summary>The period presets of the overview dashboard.</summary>
public enum OverviewPeriod
{
    All,
    Day,
    ThisWeek,
    ThisMonth,
    ThisYear,
    Last30Days,
    Custom,
}

/// <summary>An inclusive date range; a null bound is open.</summary>
public readonly record struct DateRange(DateOnly? From, DateOnly? To);

/// <summary>Resolves what an <see cref="OverviewPeriod"/> means on a given day, so the dates, the averaging unit and the averaging window are decided in one place.</summary>
public static class OverviewPeriods
{
    public static DateRange Range(OverviewPeriod period, DateOnly today, DateOnly? customFrom, DateOnly? customTo) => period switch
    {
        OverviewPeriod.All => new DateRange(null, null),
        OverviewPeriod.Day => new DateRange(today, today),
        OverviewPeriod.ThisWeek => new DateRange(CalendarWeek.StartOfWeek(today), CalendarWeek.EndOfWeek(today)),
        OverviewPeriod.ThisMonth => new DateRange(
            new DateOnly(today.Year, today.Month, 1),
            new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
        OverviewPeriod.ThisYear => new DateRange(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        OverviewPeriod.Last30Days => new DateRange(today.AddDays(-29), today),
        _ => new DateRange(customFrom, customTo),
    };

    /// <summary>The calendar unit the period's averages are taken per; periods without a natural unit average per month.</summary>
    public static AverageInterval IntervalFor(OverviewPeriod period) => period switch
    {
        OverviewPeriod.Day => AverageInterval.Day,
        OverviewPeriod.ThisWeek => AverageInterval.Week,
        OverviewPeriod.ThisYear => AverageInterval.Year,
        _ => AverageInterval.Month,
    };

    /// <summary>
    /// Where averaging starts. Only a custom range averages over its own span; every other period averages
    /// over the whole ledger up to the period's end, so the start is open (null).
    /// </summary>
    public static DateOnly? AverageFrom(OverviewPeriod period, DateOnly? effectiveFrom) =>
        period == OverviewPeriod.Custom ? effectiveFrom : null;
}
