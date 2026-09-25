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
    private TextBlock? trendLegend;
    private TextBlock? timelineRange;
    private FlowInterval trendInterval = FlowInterval.Month;

    private Border TrendPanel()
    {
        trendChart = new FlowChart();
        trendLegend = QuietText(string.Empty, 11);
        var interval = new ComboBox
        {
            ItemsSource = Enum.GetValues<FlowInterval>(),
            SelectedItem = trendInterval,
            MinWidth = 85,
        };
        interval.SelectionChanged += (_, _) =>
        {
            if (interval.SelectedItem is FlowInterval selected)
            {
                trendInterval = selected;
                UpdateAnalyticsCharts();
            }
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        AddColumn(header, Heading("TREND", 13), 0);
        AddColumn(header, interval, 1);
        return Panel(new StackPanel
        {
            Spacing = 7,
            Children = { header, trendLegend, trendChart },
        });
    }

    private Border TimelinePanel()
    {
        timelineChart = new FlowChart();
        timelineRange = QuietText(string.Empty, 10);
        timelineRange.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var panel = Panel(new StackPanel
        {
            Spacing = 7,
            Children = { Heading("TIMELINE", 13), timelineRange, timelineChart },
        });
        UpdateAnalyticsCharts();
        return panel;
    }

    private void UpdateAnalyticsCharts()
    {
        if (overviewAnalytics is not { } data || trendChart is null || timelineChart is null)
        {
            return;
        }

        var incomeOnly = analyticsFilter.Kind == TransactionKind.Income ||
            analyticsFilter.Kind is null && analyticsFilter.CategoryId is not null &&
            data.Current.Concat(data.Previous).Any(day => day.Income != Money.Zero) &&
            !data.Current.Concat(data.Previous).Any(day => day.Expenses != Money.Zero);
        var expenseOnly = analyticsFilter.Kind == TransactionKind.Expense ||
            analyticsFilter.Kind is null && analyticsFilter.CategoryId is not null &&
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
        trendLegend!.Text = incomeOnly ? "Income" : expenseOnly ? "Expense" : "Income (green) · Expense (red) · Savings (blue)";
        trendLegend.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        trendChart.SetData(trendPoints, trendSeries, data.Current.Count == 0 ? empty ?? "No matching transactions in this period" : empty);

        // Compare expenses by default, or income when explicitly filtered to income.
        var metric = incomeOnly ? "Income" : "Expense";
        timelineRange!.Text = $"{metric} · {data.From:dd MMM yyyy} – {data.To:dd MMM yyyy}" +
            (data.PreviousFrom is null ? string.Empty : $"\nvs {data.PreviousFrom:dd MMM yyyy} – {data.PreviousTo:dd MMM yyyy}");
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
