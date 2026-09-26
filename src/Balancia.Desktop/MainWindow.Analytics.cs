using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private FlowAnalytics? overviewAnalytics;
    private HistoryFilter analyticsFilter = new();
    private FlowChart? trendChart;
    private FlowChart? timelineChart;
    private FlowInterval trendInterval = FlowInterval.Month;

    private Border TrendPanel()
    {
        trendChart = new FlowChart();
        var intervalLabel = QuietText(trendInterval.ToString(), 11);
        intervalLabel.Height = 16;
        intervalLabel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        intervalLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var panel = Panel(new StackPanel
        {
            Spacing = 7,
            Children = { intervalLabel, trendChart },
        });
        panel.PointerWheelChanged += (_, args) =>
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
                intervalLabel.Text = trendInterval.ToString();
                UpdateAnalyticsCharts();
            }

            args.Handled = true;
        };
        return panel;
    }

    private Border TimelinePanel()
    {
        timelineChart = new FlowChart();
        return Panel(new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new Border { Height = 16 },
                timelineChart,
            },
        });
    }

    private void UpdateAnalyticsCharts()
    {
        if (overviewAnalytics is not { } data || trendChart is null || timelineChart is null)
        {
            return;
        }

        var incomeOnly = analyticsFilter.Kind == TransactionKind.Income ||
            analyticsFilter.Kind is null && analyticsFilter.HasCategoryFilter &&
            data.Current.Concat(data.Previous).Any(day => day.Income != Money.Zero) &&
            !data.Current.Concat(data.Previous).Any(day => day.Expenses != Money.Zero);
        var expenseOnly = analyticsFilter.Kind == TransactionKind.Expense ||
            analyticsFilter.Kind is null && analyticsFilter.HasCategoryFilter &&
            !data.Current.Concat(data.Previous).Any(day => day.Income != Money.Zero);
        var empty = analyticsFilter.Kind == TransactionKind.Transfer ? "Transfers do not count as income or expenses." :
            data.Current.Count == 0 && data.Previous.Count == 0 ? "No matching transactions" : null;
        var buckets = data.Trend(trendInterval);
        IReadOnlyList<FlowChartSeries> trendSeries = incomeOnly ? [new("Income", "#2C8B6D", true)] :
            expenseOnly ? [new("Expense", "#EF8072", true)] :
            [new("Income", "#2C8B6D", true), new("Expense", "#EF8072", true), new("Savings", "#289FCC")];
        var trendPoints = buckets.Select((bucket, index) =>
        {
            Money[] amounts = incomeOnly ? [bucket.Income] : expenseOnly ? [bucket.Expenses] :
                [bucket.Income, -bucket.Expenses, bucket.Savings];
            var details = $"{bucket.From:dd MMM yyyy} – {bucket.To:dd MMM yyyy}" + Environment.NewLine +
                string.Join(Environment.NewLine, trendSeries.Select((series, seriesIndex) => $"{series.Name}: {AmountText(amounts[seriesIndex])}"));
            return new FlowChartPoint((index + 0.5) / buckets.Count,
                bucket.From.ToString(trendInterval == FlowInterval.Month || data.To.DayNumber - data.From.DayNumber > 365
                    ? "MMM yy" : "dd MMM", CultureInfo.CurrentCulture), details, amounts);
        }).ToArray();
        trendChart.SetData(trendPoints, trendSeries, data.Current.Count == 0 ? empty ?? "No matching transactions in this period" : empty);

        // Compare expenses by default, or income when explicitly filtered to income.
        var metric = incomeOnly ? "Income" : "Expense";
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
        foreach (var offset in offsets)
        {
            var date = data.From.AddDays(offset);
            var previousDate = data.PreviousFrom?.AddDays(offset);
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

            var details = $"{metric} · {date:dd MMM yyyy}: {AmountText(currentTotal)}" +
                (previousDate is null ? string.Empty : $"\nPrevious · {previousDate:dd MMM yyyy}: {AmountText(previousTotal)}");
            timelinePoints.Add(new FlowChartPoint(length == 0 ? 0.5 : offset / (double)length,
                date.ToString(length > 365 ? "MMM yy" : "dd MMM", CultureInfo.CurrentCulture), details, [currentTotal, previousTotal]));
        }

        timelineChart.SetData(timelinePoints,
            [new(metric, incomeOnly ? "#2C8B6D" : "#EF8072", Steps: true), new("Previous period", "#B8C2CC", Steps: true)], empty);
    }
}
