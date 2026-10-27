using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>Which flow the charts show: both, or one when the filter implies it.</summary>
internal enum FlowFocus
{
    Both,
    Income,
    Expense,
}

/// <summary>What a chart draws: its points and series, or the message that replaces them.</summary>
internal sealed record FlowChartData(IReadOnlyList<FlowChartPoint> Points, IReadOnlyList<FlowChartSeries> Series, string? Message);

/// <summary>Turns the flow analytics of the overview into the points, series and messages of the trend and timeline charts.</summary>
internal static class FlowChartModel
{
    private const string IncomeColor = "#2C8B6D";
    private const string ExpenseColor = "#EF8072";
    private const string SavingsColor = "#289FCC";
    private const string PreviousPeriodColor = "#B8C2CC";
    internal const string DayFormat = "dd MMM yyyy";

    /// <summary>
    /// The charts narrow to a single flow when the filter implies it: an explicit kind, or a category filter whose matching transactions are all one-sided.
    /// </summary>
    public static FlowFocus Focus(TransactionsFilter filter, FlowAnalytics data)
    {
        switch (filter.Kind)
        {
            case TransactionKind.Income:
                return FlowFocus.Income;
            case TransactionKind.Expense:
                return FlowFocus.Expense;
            case not null:
                return FlowFocus.Both;
        }

        if (!filter.HasCategoryFilter)
        {
            return FlowFocus.Both;
        }

        var hasIncome = false;
        var hasExpenses = false;
        foreach (var day in data.Current.Concat(data.Previous))
        {
            hasIncome |= day.Income != Money.Zero;
            hasExpenses |= day.Expenses != Money.Zero;
        }

        return hasIncome && !hasExpenses ? FlowFocus.Income : !hasIncome ? FlowFocus.Expense : FlowFocus.Both;
    }

    /// <summary>The message that replaces both charts, or null when there is something to draw.</summary>
    public static string? EmptyMessage(TransactionsFilter filter, FlowAnalytics data) =>
        filter.Kind == TransactionKind.Transfer ? Get("Transfers do not count as income or expenses.") :
        data.Current.Count == 0 && data.Previous.Count == 0 ? Get("No matching transactions") : null;

    /// <exception cref="OverflowException">A bucket total exceeds the monetary limits.</exception>
    public static FlowChartData Trend(FlowAnalytics data, FlowInterval interval, FlowFocus focus, string? empty)
    {
        IReadOnlyList<FlowBucket> buckets;
        try
        {
            buckets = data.Trend(interval);
        }
        catch (OverflowException exception)
        {
            Serilog.Log.Error(exception,
                "Flow aggregation exceeded monetary limits, Interval: '{Interval}', From: '{From}', To: '{To}', FlowCount: '{FlowCount}'",
                interval, data.From, data.To, data.Current.Count);
            throw;
        }

        var incomeOnly = focus == FlowFocus.Income;
        var expenseOnly = focus == FlowFocus.Expense;
        IReadOnlyList<FlowChartSeries> series = incomeOnly
            ? [new FlowChartSeries(Get("Income"), IncomeColor, true)]
            : expenseOnly
                ? [new FlowChartSeries(Get("Expense"), ExpenseColor, true)]
                : [new FlowChartSeries(Get("Income"), IncomeColor, true),
                    new FlowChartSeries(Get("Expense"), ExpenseColor, true),
                    new FlowChartSeries(Get("Savings"), SavingsColor)];
        var labelFormat = interval == FlowInterval.Month || data.To.DayNumber - data.From.DayNumber > 365 ? "MMM yy" : "dd MMM";
        var points = buckets.Select((bucket, index) =>
        {
            Money[] amounts = incomeOnly ? [bucket.Income] : expenseOnly ? [bucket.Expenses] :
                [bucket.Income, -bucket.Expenses, bucket.Savings];
            var heading = $"{DayText(bucket.From)} – {DayText(bucket.To)}";
            var details = heading + Environment.NewLine +
                string.Join(Environment.NewLine, series.Select((item, seriesIndex) => $"{item.Name}: {AmountText(amounts[seriesIndex])}"));
            return new FlowChartPoint((index + 0.5) / buckets.Count, bucket.From.ToString(labelFormat, Culture),
                details, amounts, heading);
        }).ToArray();
        return new FlowChartData(points, series, data.Current.Count == 0 ? empty ?? Get("No matching transactions in this period") : empty);
    }

    /// <summary>Cumulative totals of the current period against the previous one, aligned by day offset.</summary>
    public static FlowChartData Timeline(FlowAnalytics data, bool incomeOnly, string? empty)
    {
        // Compare expenses by default, or income when explicitly filtered to income.
        var metric = incomeOnly ? Get("Income") : Get("Expense");
        var currentLabel = Get("Current period");
        var previousLabel = Get("Previous period");
        var length = data.To.DayNumber - data.From.DayNumber;
        var labelFormat = length > 365 ? "MMM yy" : "dd MMM";
        var offsets = length < 10000 ? Enumerable.Range(0, length + 1).ToArray() :
            data.Current.Select(day => day.Date.DayNumber - data.From.DayNumber)
            .Concat(data.Previous.Select(day => day.Date.DayNumber - data.PreviousFrom!.Value.DayNumber))
            .Append(0).Append(length).Distinct().Order().ToArray();
        var currentIndex = 0;
        var previousIndex = 0;
        var currentTotal = Money.Zero;
        var previousTotal = Money.Zero;
        var points = new List<FlowChartPoint>(offsets.Length);
        foreach (var dayOffset in offsets)
        {
            var date = data.From.AddDays(dayOffset);
            var previousDate = data.PreviousFrom?.AddDays(dayOffset);
            currentTotal = Accumulate(data.Current, ref currentIndex, date, incomeOnly, currentTotal);
            previousTotal = Accumulate(data.Previous, ref previousIndex, previousDate, incomeOnly, previousTotal);

            var dateText = DayText(date);
            var details = $"{metric} · {dateText}: {AmountText(currentTotal)}";
            IReadOnlyList<string> tooltipLabels = [currentLabel];
            IReadOnlyList<string> tooltipDates = [dateText];
            if (previousDate is { } comparisonDate)
            {
                var previousText = $"{previousLabel} · {DayText(comparisonDate)}";
                details += $"\n{previousText}: {AmountText(previousTotal)}";
                tooltipLabels = [currentLabel, previousLabel];
                tooltipDates = [dateText, DayText(comparisonDate)];
            }

            points.Add(new FlowChartPoint(length == 0 ? 0.5 : dayOffset / (double)length,
                date.ToString(labelFormat, Culture), details, [currentTotal, previousTotal], metric, tooltipLabels, tooltipDates));
        }

        return new FlowChartData(points,
            [new FlowChartSeries(metric, incomeOnly ? IncomeColor : ExpenseColor, Steps: true),
                new FlowChartSeries(previousLabel, PreviousPeriodColor, Steps: true)], empty);
    }

    private static Money Accumulate(IReadOnlyList<DailyFlow> days, ref int index, DateOnly? upTo, bool income, Money total)
    {
        while (index < days.Count && days[index].Date <= upTo)
        {
            var day = days[index++];
            total += income ? day.Income : day.Expenses;
        }

        return total;
    }

    internal static string DayText(DateOnly date) => date.ToString(DayFormat, Culture);
}
