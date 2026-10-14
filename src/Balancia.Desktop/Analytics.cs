using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const string IncomeColor = "#2C8B6D";
    private const string ExpenseColor = "#EF8072";
    private const string SavingsColor = "#289FCC";
    private const string PreviousPeriodColor = "#B8C2CC";
    private const string DayFormat = "dd MMM yyyy";

    private FlowAnalytics? overviewAnalytics;
    private TransactionsFilter analyticsFilter = new();
    private FlowInterval trendInterval = FlowInterval.Week;

    private void InitializeTrendPanel()
    {
        RefreshTrendIntervalLabel();
        TrendPanelBorder.PointerWheelChanged += (_, args) =>
        {
            var zoomOut = args.Delta.Y > 0;
            var nextInterval = (trendInterval, zoomOut) switch
            {
                (FlowInterval.Day, true) => FlowInterval.Week,
                (FlowInterval.Week, true) => FlowInterval.Month,
                (FlowInterval.Month, false) => FlowInterval.Week,
                (FlowInterval.Week, false) => FlowInterval.Day,
                _ => trendInterval,
            };
            if (SetTrendInterval(nextInterval))
            {
                UpdateAnalyticsCharts();
            }

            args.Handled = true;
        };
    }

    private void RefreshTrendIntervalLabel() => TrendIntervalLabel.Text = Get(trendInterval.ToString());

    private bool SetTrendInterval(FlowInterval interval)
    {
        if (interval == trendInterval)
        {
            return false;
        }

        trendInterval = interval;
        RefreshTrendIntervalLabel();
        return true;
    }

    private void SetDefaultTrendInterval(OverviewPeriod period, DateOnly? from, DateOnly? to)
    {
        SetTrendInterval(period switch
        {
            OverviewPeriod.Day or OverviewPeriod.ThisWeek => FlowInterval.Day,
            OverviewPeriod.ThisMonth or OverviewPeriod.Last30Days => FlowInterval.Week,
            OverviewPeriod.ThisYear or OverviewPeriod.All => FlowInterval.Month,
            _ when from is { } start && to is { } end && end < start.AddMonths(1) => FlowInterval.Day,
            _ when from is { } start && to is { } end && end < start.AddYears(1) => FlowInterval.Week,
            _ => FlowInterval.Month,
        });
    }

    private void UpdateAnalyticsCharts()
    {
        if (overviewAnalytics is not { } data)
        {
            return;
        }

        var (incomeOnly, expenseOnly) = ResolveFlowFocus(data);
        var empty = analyticsFilter.Kind == TransactionKind.Transfer ? Get("Transfers do not count as income or expenses.") :
            data.Current.Count == 0 && data.Previous.Count == 0 ? Get("No matching transactions") : null;

        UpdateTrendChart(data, incomeOnly, expenseOnly, empty);
        UpdateTimelineChart(data, incomeOnly, empty);
    }

    // The charts narrow to a single flow when the filter implies it: an explicit kind,
    // or a category filter whose matching transactions are all one-sided.
    private (bool IncomeOnly, bool ExpenseOnly) ResolveFlowFocus(FlowAnalytics data)
    {
        switch (analyticsFilter.Kind)
        {
            case TransactionKind.Income:
                return (true, false);
            case TransactionKind.Expense:
                return (false, true);
            case not null:
                return (false, false);
        }

        if (!analyticsFilter.HasCategoryFilter)
        {
            return (false, false);
        }

        var hasIncome = false;
        var hasExpenses = false;
        foreach (var day in data.Current.Concat(data.Previous))
        {
            hasIncome |= day.Income != Money.Zero;
            hasExpenses |= day.Expenses != Money.Zero;
        }

        return (hasIncome && !hasExpenses, !hasIncome);
    }

    private void UpdateTrendChart(FlowAnalytics data, bool incomeOnly, bool expenseOnly, string? empty)
    {
        IReadOnlyList<FlowBucket> buckets;
        try
        {
            buckets = data.Trend(trendInterval);
        }
        catch (OverflowException exception)
        {
            Serilog.Log.Error(exception,
                "Flow aggregation exceeded monetary limits, Interval: '{Interval}', From: '{From}', To: '{To}', FlowCount: '{FlowCount}'",
                trendInterval, data.From, data.To, data.Current.Count);
            throw;
        }

        IReadOnlyList<FlowChartSeries> trendSeries = incomeOnly
            ? [new FlowChartSeries(Get("Income"), IncomeColor, true)]
            : expenseOnly
                ? [new FlowChartSeries(Get("Expense"), ExpenseColor, true)]
                : [new FlowChartSeries(Get("Income"), IncomeColor, true),
                    new FlowChartSeries(Get("Expense"), ExpenseColor, true),
                    new FlowChartSeries(Get("Savings"), SavingsColor)];
        var labelFormat = trendInterval == FlowInterval.Month || data.To.DayNumber - data.From.DayNumber > 365
            ? "MMM yy" : "dd MMM";
        var trendPoints = buckets.Select((bucket, index) =>
        {
            Money[] amounts = incomeOnly ? [bucket.Income] : expenseOnly ? [bucket.Expenses] :
                [bucket.Income, -bucket.Expenses, bucket.Savings];
            var heading = $"{DayText(bucket.From)} – {DayText(bucket.To)}";
            var details = heading + Environment.NewLine +
                string.Join(Environment.NewLine, trendSeries.Select((series, seriesIndex) => $"{series.Name}: {AmountText(amounts[seriesIndex])}"));
            return new FlowChartPoint((index + 0.5) / buckets.Count, bucket.From.ToString(labelFormat, Culture),
                details, amounts, heading);
        }).ToArray();
        TrendChart.SetData(trendPoints, trendSeries, data.Current.Count == 0 ? empty ?? Get("No matching transactions in this period") : empty);
    }

    // Cumulative totals of the current period against the previous one, aligned by day offset.
    private void UpdateTimelineChart(FlowAnalytics data, bool incomeOnly, string? empty)
    {
        // Compare expenses by default, or income when explicitly filtered to income.
        var metric = incomeOnly ? Get("Income") : Get("Expense");
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
        var timelinePoints = new List<FlowChartPoint>(offsets.Length);
        foreach (var dayOffset in offsets)
        {
            var date = data.From.AddDays(dayOffset);
            var previousDate = data.PreviousFrom?.AddDays(dayOffset);
            currentTotal = Accumulate(data.Current, ref currentIndex, date, incomeOnly, currentTotal);
            previousTotal = Accumulate(data.Previous, ref previousIndex, previousDate, incomeOnly, previousTotal);

            var dateText = DayText(date);
            var details = $"{metric} · {dateText}: {AmountText(currentTotal)}";
            IReadOnlyList<string> tooltipLabels = [dateText];
            if (previousDate is { } comparisonDate)
            {
                var previousText = $"{previousLabel} · {DayText(comparisonDate)}";
                details += $"\n{previousText}: {AmountText(previousTotal)}";
                tooltipLabels = [dateText, previousText];
            }

            timelinePoints.Add(new FlowChartPoint(length == 0 ? 0.5 : dayOffset / (double)length,
                date.ToString(labelFormat, Culture), details, [currentTotal, previousTotal], metric, tooltipLabels));
        }

        TimelineChart.SetData(timelinePoints,
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

    private static string DayText(DateOnly date) => date.ToString(DayFormat, Culture);
}
