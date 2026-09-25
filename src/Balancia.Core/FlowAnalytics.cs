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
    public IReadOnlyList<FlowBucket> Trend(FlowInterval interval)
    {
        var buckets = new List<FlowBucket>();
        var index = 0;
        var start = From;
        while (start <= To)
        {
            var length = interval switch
            {
                FlowInterval.Day => 1,
                FlowInterval.Week => 7 - ((int)start.DayOfWeek + 6) % 7,
                _ => DateTime.DaysInMonth(start.Year, start.Month) - start.Day + 1,
            };
            var end = DateOnly.FromDayNumber(Math.Min(To.DayNumber, start.DayNumber + length - 1));
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
}
