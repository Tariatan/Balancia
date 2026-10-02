using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private FlowAnalytics? overviewAnalytics;
    private TransactionsFilter analyticsFilter = new();
    private FlowInterval trendInterval = FlowInterval.Week;

    private void InitializeTrendPanel()
    {
        TrendIntervalLabel.Text = Get(trendInterval.ToString());
        TrendPanelBorder.PointerWheelChanged += (_, args) =>
        {
            var nextInterval = (trendInterval, args.Delta.Y > 0) switch
            {
                (FlowInterval.Day, true) => FlowInterval.Week,
                (FlowInterval.Week, true) => FlowInterval.Month,
                (FlowInterval.Month, false) => FlowInterval.Week,
                (FlowInterval.Week, false) => FlowInterval.Day,
                _ => trendInterval,
            };
            if (nextInterval != trendInterval)
            {
                trendInterval = nextInterval;
                TrendIntervalLabel.Text = Get(trendInterval.ToString());
                UpdateAnalyticsCharts();
            }

            args.Handled = true;
        };
    }

    private void LocalizeTrendPanel()
    {
        TrendIntervalLabel.Text = Get(trendInterval.ToString());
    }

    private void SetDefaultTrendInterval(OverviewPeriod period, DateOnly? from, DateOnly? to)
    {
        var interval = period switch
        {
            OverviewPeriod.Day or OverviewPeriod.ThisWeek => FlowInterval.Day,
            OverviewPeriod.ThisMonth or OverviewPeriod.Last30Days => FlowInterval.Week,
            OverviewPeriod.ThisYear or OverviewPeriod.All => FlowInterval.Month,
            _ when from is { } start && to is { } end && end < start.AddMonths(1) => FlowInterval.Day,
            _ when from is { } start && to is { } end && end < start.AddYears(1) => FlowInterval.Week,
            _ => FlowInterval.Month,
        };
        if (interval == trendInterval)
        {
            return;
        }

        trendInterval = interval;
        TrendIntervalLabel.Text = Get(trendInterval.ToString());
    }

    private void UpdateAnalyticsCharts()
    {
        if (overviewAnalytics is not { } data)
        {
            return;
        }

        var incomeOnly = analyticsFilter.Kind == TransactionKind.Income ||
            analyticsFilter.Kind is null && analyticsFilter.HasCategoryFilter &&
            data.Current.Concat(data.Previous).Any(day => day.Income != Money.Zero)
            && data.Current.Concat(data.Previous).All(day => day.Expenses == Money.Zero);
        var expenseOnly = analyticsFilter.Kind == TransactionKind.Expense ||
            analyticsFilter.Kind is null
            && analyticsFilter.HasCategoryFilter
            && data.Current.Concat(data.Previous).All(day => day.Income == Money.Zero);
        var empty = analyticsFilter.Kind == TransactionKind.Transfer ? Get("Transfers do not count as income or expenses.") :
            data.Current.Count == 0 && data.Previous.Count == 0 ? Get("No matching transactions") : null;
        var buckets = data.Trend(trendInterval);
        IReadOnlyList<FlowChartSeries> trendSeries = incomeOnly
            ? [new FlowChartSeries(Get("Income"), "#2C8B6D", true)]
            : expenseOnly
                ? [new FlowChartSeries(Get("Expense"), "#EF8072", true)]
                : [new FlowChartSeries(Get("Income"), "#2C8B6D", true),
                    new FlowChartSeries(Get("Expense"), "#EF8072", true),
                    new FlowChartSeries(Get("Savings"), "#289FCC")];
        var trendPoints = buckets.Select((bucket, index) =>
        {
            Money[] amounts = incomeOnly ? [bucket.Income] : expenseOnly ? [bucket.Expenses] :
                [bucket.Income, -bucket.Expenses, bucket.Savings];
            var details = $"{bucket.From.ToString("dd MMM yyyy", Culture)} – {bucket.To.ToString("dd MMM yyyy", Culture)}" + Environment.NewLine +
                string.Join(Environment.NewLine, trendSeries.Select((series, seriesIndex) => $"{series.Name}: {AmountText(amounts[seriesIndex])}"));
            return new FlowChartPoint((index + 0.5) / buckets.Count,
                bucket.From.ToString(trendInterval == FlowInterval.Month || data.To.DayNumber - data.From.DayNumber > 365
                    ? "MMM yy" : "dd MMM", Culture), details, amounts);
        }).ToArray();
        TrendChart.SetData(trendPoints, trendSeries, data.Current.Count == 0 ? empty ?? Get("No matching transactions in this period") : empty);

        // Compare expenses by default, or income when explicitly filtered to income.
        var metric = incomeOnly ? Get("Income") : Get("Expense");
        var length = data.To.DayNumber - data.From.DayNumber;
        var offsets = length < 10000 ? Enumerable.Range(0, length + 1).ToArray() :
            data.Current.Select(day => day.Date.DayNumber - data.From.DayNumber)
            .Concat(data.Previous.Select(day => day.Date.DayNumber - data.PreviousFrom!.Value.DayNumber))
            .Append(0).Append(length).Distinct().Order().ToArray();
        var currentIndex = 0;
        var previousIndex = 0;
        var currentTotal = Money.Zero;
        var previousTotal = Money.Zero;
        var timelinePoints = new List<FlowChartPoint>();
        foreach (var dayOffset in offsets)
        {
            var date = data.From.AddDays(dayOffset);
            var previousDate = data.PreviousFrom?.AddDays(dayOffset);
            while (currentIndex < data.Current.Count && data.Current[currentIndex].Date <= date)
            {
                var day = data.Current[currentIndex++];
                currentTotal += incomeOnly ? day.Income : day.Expenses;
            }

            while (previousIndex < data.Previous.Count && data.Previous[previousIndex].Date <= previousDate)
            {
                var day = data.Previous[previousIndex++];
                previousTotal += incomeOnly ? day.Income : day.Expenses;
            }

            var details = $"{metric} · {date.ToString("dd MMM yyyy", Culture)}: {AmountText(currentTotal)}" +
                (previousDate is null ? string.Empty : $"\n{Get("Previous period")} · {previousDate.Value.ToString("dd MMM yyyy", Culture)}: {AmountText(previousTotal)}");
            timelinePoints.Add(new FlowChartPoint(length == 0 ? 0.5 : dayOffset / (double)length,
                date.ToString(length > 365 ? "MMM yy" : "dd MMM", Culture), details, [currentTotal, previousTotal]));
        }

        TimelineChart.SetData(timelinePoints,
            [new FlowChartSeries(metric, incomeOnly ? "#2C8B6D" : "#EF8072", Steps: true),
                new FlowChartSeries(Get("Previous period"), "#B8C2CC", Steps: true)], empty);
    }
}
