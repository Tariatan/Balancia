using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;
using Avalonia.Headless.XUnit;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class FlowChartModelTests
{
    // Chart label assertions use English regardless of the language left by another test.
    public FlowChartModelTests() => SetLanguage("en");

    private static readonly DateOnly Start = new(2026, 10, 1);

    private static DailyFlow Day(int day, decimal income = 0m, decimal expenses = 0m) =>
        new(new DateOnly(2026, 10, day), Money.FromFrancs(income), Money.FromFrancs(expenses));

    private static FlowAnalytics Analytics(IReadOnlyList<DailyFlow> current, IReadOnlyList<DailyFlow>? previous = null, int lastDay = 14,
        DateOnly? previousFrom = null) =>
        new(Start, new DateOnly(2026, 10, lastDay), previousFrom, previousFrom?.AddDays(lastDay - 1), current, previous ?? []);

    private static readonly TransactionsFilter NoFilter = new();

    // Focus

    [AvaloniaTheory]
    [InlineData(TransactionKind.Income, "Income")]
    [InlineData(TransactionKind.Expense, "Expense")]
    [InlineData(TransactionKind.Transfer, "Both")]
    public void Focus_ExplicitKind_NarrowsToThatKind(TransactionKind kind, string expected)
    {
        // Arrange
        var data = Analytics([Day(2, income: 10m, expenses: 5m)]);

        // Act
        var focus = FlowChartModel.Focus(new TransactionsFilter(Kind: kind), data);

        // Assert
        Assert.Equal(Enum.Parse<FlowFocus>(expected), focus);
    }

    [AvaloniaFact]
    public void Focus_NoKindAndNoCategory_ShowsBoth()
    {
        // Arrange
        var data = Analytics([Day(2, income: 10m)]);

        // Act
        var focus = FlowChartModel.Focus(NoFilter, data);

        // Assert
        Assert.Equal(FlowFocus.Both, focus);
    }

    [AvaloniaTheory]
    [InlineData(10, 0, "Income")] // only income matches the category
    [InlineData(0, 10, "Expense")] // only expenses match
    [InlineData(10, 5, "Both")] // both flows match
    [InlineData(0, 0, "Expense")] // nothing matches: expenses by default
    public void Focus_CategoryFilter_FollowsWhichFlowsTheCategoryHas(int income, int expenses, string expected)
    {
        // Arrange
        var data = Analytics([Day(2, income, expenses)]);

        // Act
        var focus = FlowChartModel.Focus(new TransactionsFilter(CategoryId: "food"), data);

        // Assert
        Assert.Equal(Enum.Parse<FlowFocus>(expected), focus);
    }

    [AvaloniaFact]
    public void Focus_CategoryFilter_AlsoCountsThePreviousPeriod()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 5m)], [new DailyFlow(new DateOnly(2026, 9, 2), Money.FromFrancs(8m), Money.Zero)], previousFrom: new DateOnly(2026, 9, 1));

        // Act
        var focus = FlowChartModel.Focus(new TransactionsFilter(CategoryIds: ["food", "rent"]), data);

        // Assert
        Assert.Equal(FlowFocus.Both, focus);
    }

    // EmptyMessage

    [AvaloniaFact]
    public void EmptyMessage_Transfers_SayTheyAreNotIncomeOrExpenses()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 5m)]);

        // Act
        var message = FlowChartModel.EmptyMessage(new TransactionsFilter(Kind: TransactionKind.Transfer), data);

        // Assert
        Assert.Equal(Get("Transfers do not count as income or expenses."), message);
    }

    [AvaloniaFact]
    public void EmptyMessage_NoFlowsInEitherPeriod_SaysNothingMatches()
    {
        // Arrange
        var data = Analytics([]);

        // Act
        var message = FlowChartModel.EmptyMessage(NoFilter, data);

        // Assert
        Assert.Equal(Get("No matching transactions"), message);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EmptyMessage_AFlowInEitherPeriod_IsNull(bool inCurrent, bool inPrevious)
    {
        // Arrange
        var current = inCurrent ? new[] { Day(2, expenses: 5m) } : [];
        var previous = inPrevious ? new[] { new DailyFlow(new DateOnly(2026, 9, 2), Money.Zero, Money.FromFrancs(3m)) } : [];
        var data = Analytics(current, previous, previousFrom: new DateOnly(2026, 9, 1));

        // Act
        var message = FlowChartModel.EmptyMessage(NoFilter, data);

        // Assert
        Assert.Null(message);
    }

    // Trend

    [AvaloniaFact]
    public void Trend_ShowingBoth_HasIncomeExpenseAndSavingsSeriesAndOnePointPerBucket()
    {
        // Arrange
        var data = Analytics([Day(2, income: 100m, expenses: 30m), Day(9, expenses: 20m)]);

        // Act
        var chart = FlowChartModel.Trend(data, FlowInterval.Week, FlowFocus.Both, null);

        // Assert
        Assert.Equal([Get("Income"), Get("Expense"), Get("Savings")], chart.Series.Select(series => series.Name));
        Assert.Equal([true, true, false], chart.Series.Select(series => series.Bars));
        Assert.Equal(data.Trend(FlowInterval.Week).Count, chart.Points.Count);
        Assert.Null(chart.Message);
    }

    [AvaloniaFact]
    public void Trend_ShowingBoth_PlotsIncomeNegatedExpensesAndSavings()
    {
        // Arrange
        var data = Analytics([Day(2, income: 100m, expenses: 30m)], lastDay: 3);

        // Act
        var chart = FlowChartModel.Trend(data, FlowInterval.Month, FlowFocus.Both, null);

        // Assert
        var point = Assert.Single(chart.Points);
        Assert.Equal([Money.FromFrancs(100m), Money.FromFrancs(-30m), Money.FromFrancs(70m)], point.Values);
    }

    [AvaloniaTheory]
    [InlineData("Income", 100)]
    [InlineData("Expense", 30)]
    public void Trend_ShowingOneFlow_HasASingleBarSeriesOfThatFlow(string flow, int expected)
    {
        // Arrange
        var data = Analytics([Day(2, income: 100m, expenses: 30m)], lastDay: 3);

        // Act
        var chart = FlowChartModel.Trend(data, FlowInterval.Month, Enum.Parse<FlowFocus>(flow), null);

        // Assert
        var series = Assert.Single(chart.Series);
        Assert.Equal(Get(flow), series.Name);
        Assert.True(series.Bars);
        Assert.Equal([Money.FromFrancs(expected)], Assert.Single(chart.Points).Values);
    }

    [AvaloniaFact]
    public void Trend_Points_AreCenteredInTheirSlotsWithTheBucketsRangeAsHeading()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 5m)], lastDay: 14);

        // Act
        var chart = FlowChartModel.Trend(data, FlowInterval.Day, FlowFocus.Both, null);

        // Assert
        Assert.Equal(14, chart.Points.Count);
        Assert.Equal(0.5 / 14, chart.Points[0].Position, 10);
        Assert.Equal(13.5 / 14, chart.Points[13].Position, 10);
        Assert.Equal("02 Oct 2026 – 02 Oct 2026", chart.Points[1].TooltipHeading);
        Assert.StartsWith("02 Oct 2026 – 02 Oct 2026", chart.Points[1].Details);
        Assert.Contains($"{Get("Expense")}: -5.00", chart.Points[1].Details);
    }

    [AvaloniaTheory]
    [InlineData(FlowInterval.Day, 14, "01 Oct")]
    [InlineData(FlowInterval.Month, 14, "Oct 26")]
    [InlineData(FlowInterval.Day, 365, "01 Oct")]
    [InlineData(FlowInterval.Day, 366, "Oct 26")]
    [InlineData(FlowInterval.Day, 400, "Oct 26")]
    public void Trend_BucketLabels_UseTheMonthFormatForMonthsAndLongRanges(FlowInterval interval, int days, string expected)
    {
        // Arrange
        var data = new FlowAnalytics(Start, Start.AddDays(days), null, null, [Day(2, expenses: 5m)], []);

        // Act
        var chart = FlowChartModel.Trend(data, interval, FlowFocus.Both, null);

        // Assert
        Assert.Equal(expected, chart.Points[0].Label);
    }

    [AvaloniaFact]
    public void Trend_WithoutCurrentFlows_FallsBackToThePeriodMessageUnlessOneIsGiven()
    {
        // Arrange
        var data = Analytics([]);

        // Act
        var fallback = FlowChartModel.Trend(data, FlowInterval.Week, FlowFocus.Both, null);
        var given = FlowChartModel.Trend(data, FlowInterval.Week, FlowFocus.Both, "Given");

        // Assert
        Assert.Equal(Get("No matching transactions in this period"), fallback.Message);
        Assert.Equal("Given", given.Message);
    }

    [AvaloniaFact]
    public void Trend_WithCurrentFlows_PassesTheGivenMessageThrough()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 5m)]);

        // Act
        var chart = FlowChartModel.Trend(data, FlowInterval.Week, FlowFocus.Both, "Given");

        // Assert
        Assert.Equal("Given", chart.Message);
    }

    // Timeline

    [AvaloniaFact]
    public void Timeline_Points_AreCumulativeExpensesOfTheCurrentPeriod()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 10m), Day(4, income: 99m, expenses: 5m)], lastDay: 5);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        Assert.Equal(5, chart.Points.Count);
        Assert.Equal([0m, 10m, 10m, 15m, 15m], chart.Points.Select(point => point.Values[0].Francs));
        Assert.Equal([0.0, 0.25, 0.5, 0.75, 1.0], chart.Points.Select(point => point.Position));
        Assert.Equal("01 Oct", chart.Points[0].Label);
    }

    [AvaloniaFact]
    public void Timeline_ForIncome_AccumulatesIncomeInTheIncomeColor()
    {
        // Arrange
        var data = Analytics([Day(2, income: 40m, expenses: 7m), Day(3, income: 2m)], lastDay: 3);

        // Act
        var chart = FlowChartModel.Timeline(data, true, null);

        // Assert
        Assert.Equal([0m, 40m, 42m], chart.Points.Select(point => point.Values[0].Francs));
        Assert.Equal(Get("Income"), chart.Series[0].Name);
        Assert.Same(Palette.ChartIncome, chart.Series[0].Brush);
        Assert.All(chart.Series, series => Assert.True(series.Steps));
    }

    [AvaloniaFact]
    public void Timeline_ForExpenses_UsesTheExpenseColorAndThePreviousPeriodSeries()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 1m)], lastDay: 2);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        Assert.Equal([Get("Expense"), Get("Previous period")], chart.Series.Select(series => series.Name));
        Assert.Equal<IBrush>([Palette.ChartExpense, Palette.ChartPrevious], chart.Series.Select(series => series.Brush));
    }

    [AvaloniaFact]
    public void Timeline_WithAPreviousPeriod_AlignsItByDayOffsetAndNamesBothDatesInTheTooltip()
    {
        // Arrange
        var previousFrom = new DateOnly(2026, 9, 1);
        var data = new FlowAnalytics(Start, Start.AddDays(2), previousFrom, previousFrom.AddDays(2), [Day(2, expenses: 10m)],
            [new DailyFlow(new DateOnly(2026, 9, 2), Money.Zero, Money.FromFrancs(4m)), new DailyFlow(new DateOnly(2026, 9, 3), Money.Zero, Money.FromFrancs(6m))]);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        Assert.Equal([0m, 4m, 10m], chart.Points.Select(point => point.Values[1].Francs));
        var middle = chart.Points[1];
        Assert.Equal([Get("Current period"), Get("Previous period")], middle.TooltipLabels);
        Assert.Equal(["02 Oct 2026", "02 Sep 2026"], middle.TooltipDates);
        Assert.Contains($"{Get("Previous period")} · 02 Sep 2026: 4.00", middle.Details);
        Assert.StartsWith($"{Get("Expense")} · 02 Oct 2026: 10.00", middle.Details);
    }

    [AvaloniaFact]
    public void Timeline_WithoutAPreviousPeriod_HasOnlyTheCurrentLabelInTheTooltip()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 10m)], lastDay: 3);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        Assert.All(chart.Points, point => Assert.Equal([Get("Current period")], point.TooltipLabels));
        Assert.All(chart.Points, point => Assert.DoesNotContain(Get("Previous period"), point.Details));
    }

    [AvaloniaFact]
    public void Timeline_ForASingleDay_CentersTheOnlyPoint()
    {
        // Arrange
        var data = new FlowAnalytics(Start, Start, null, null, [Day(1, expenses: 3m)], []);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        var point = Assert.Single(chart.Points);
        Assert.Equal(0.5, point.Position);
        Assert.Equal(Money.FromFrancs(3m), point.Values[0]);
    }

    [AvaloniaFact]
    public void Timeline_ForAVeryLongRange_PlotsOnlyTheDaysWithFlowsPlusBothEnds()
    {
        // Arrange
        var from = new DateOnly(1990, 1, 1);
        var to = new DateOnly(2026, 1, 1);
        var data = new FlowAnalytics(from, to, null, null,
            [new DailyFlow(new DateOnly(2000, 6, 1), Money.Zero, Money.FromFrancs(5m)), new DailyFlow(new DateOnly(2010, 6, 1), Money.Zero, Money.FromFrancs(7m))], []);

        // Act
        var chart = FlowChartModel.Timeline(data, false, null);

        // Assert
        Assert.Equal([0m, 5m, 12m, 12m], chart.Points.Select(point => point.Values[0].Francs));
        Assert.Equal(0.0, chart.Points[0].Position);
        Assert.Equal(1.0, chart.Points[^1].Position);
        Assert.Equal("Jan 90", chart.Points[0].Label);
    }

    [AvaloniaFact]
    public void Timeline_PassesTheGivenMessageThrough()
    {
        // Arrange
        var data = Analytics([Day(2, expenses: 1m)]);

        // Act
        var chart = FlowChartModel.Timeline(data, false, "Given");

        // Assert
        Assert.Equal("Given", chart.Message);
        Assert.Null(FlowChartModel.Timeline(data, false, null).Message);
    }
}
