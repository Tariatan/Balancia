namespace Balancia.Core;

public enum FlowInterval
{
    Day,
    Week,
    Month,
}

public sealed record DailyFlow(DateOnly Date, Money Income, Money Expenses);

public sealed record FlowBucket(DateOnly From, DateOnly To, Money Income, Money Expenses)
{
    public Money Savings => Income - Expenses;
}

public sealed record FlowAnalytics(DateOnly From, DateOnly To, DateOnly? PreviousFrom,
    DateOnly? PreviousTo, IReadOnlyList<DailyFlow> Current, IReadOnlyList<DailyFlow> Previous)
{
    /// <exception cref="OverflowException">A bucket total exceeds the monetary limits.</exception>
    public IReadOnlyList<FlowBucket> Trend(FlowInterval interval)
    {
        // Current is expected to be sorted by date ascending, so one forward pass fills every bucket.
        List<FlowBucket> buckets = [];
        var index = 0;
        var start = From;
        while (start <= To)
        {
            var naturalEnd = IntervalEnd(interval, start);
            var end = naturalEnd < To ? naturalEnd : To;
            var income = Money.Zero;
            var expenses = Money.Zero;
            while (index < Current.Count && Current[index].Date <= end)
            {
                income += Current[index].Income;
                expenses += Current[index].Expenses;
                index++;
            }

            buckets.Add(new FlowBucket(start, end, income, expenses));
            if (end == To)
            {
                break;
            }

            start = end.AddDays(1);
        }

        return buckets;
    }

    private static DateOnly IntervalEnd(FlowInterval interval, DateOnly start) => interval switch
    {
        FlowInterval.Day => start,
        FlowInterval.Week => CalendarWeek.EndOfWeek(start),
        FlowInterval.Month => new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown flow interval."),
    };
}
