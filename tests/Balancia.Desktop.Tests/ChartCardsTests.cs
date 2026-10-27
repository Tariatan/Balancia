using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Balancia.Core;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class ChartCardsTests
{
    private static (Window Window, T Card) Show<T>(T card) where T : Control
    {
        var window = new Window { Width = 520, Height = 320, Content = card };
        window.Show();
        return (window, card);
    }

    private static Point Center(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static FlowChartData Data(string? message = null) =>
        new([new FlowChartPoint(0.5, "01 Oct", "details", [Money.FromFrancs(5m)])], [new FlowChartSeries("Expense", "#EF8072", Bars: true)], message);

    // TrendCard

    [AvaloniaFact]
    public void Trend_Render_GivesTheChartThePointsSeriesAndMessage()
    {
        // Arrange
        var (_, card) = Show(new TrendCard());
        var data = Data("Nothing here");

        // Act
        card.Render(data);

        // Assert
        var chart = card.FindControl<FlowChart>("TrendChart")!;
        Assert.Same(data.Points, chart.Points);
        Assert.Same(data.Series, chart.Series);
        Assert.Equal("Nothing here", chart.Message);
    }

    [AvaloniaTheory]
    [InlineData("Day")]
    [InlineData("Week")]
    [InlineData("Month")]
    public void Trend_ShowInterval_NamesTheIntervalAboveTheChart(string name)
    {
        // Arrange
        var (_, card) = Show(new TrendCard());

        // Act
        card.ShowInterval(Enum.Parse<FlowInterval>(name));

        // Assert
        Assert.Equal(Get(name), card.FindControl<TextBlock>("TrendIntervalLabel")!.Text);
    }

    [AvaloniaTheory]
    [InlineData(1, true)] // wheel forward: a coarser interval
    [InlineData(-1, false)] // wheel back: a finer interval
    public void Trend_Wheel_RequestsAZoomInTheWheelsDirectionAndIsHandled(int delta, bool zoomOut)
    {
        // Arrange
        var (window, card) = Show(new TrendCard());
        var requests = new List<bool>();
        card.ZoomRequested += requests.Add;
        var handled = false;
        card.AddHandler(InputElement.PointerWheelChangedEvent, (_, args) => handled = args.Handled, handledEventsToo: true);

        // Act
        window.MouseWheel(Center(window, card), new Vector(0, delta));

        // Assert
        Assert.Equal([zoomOut], requests);
        Assert.True(handled);
    }

    // TimelineCard

    [AvaloniaFact]
    public void Timeline_Render_GivesTheChartThePointsSeriesAndMessage()
    {
        // Arrange
        var (_, card) = Show(new TimelineCard());
        var data = Data("Nothing here");

        // Act
        card.Render(data);

        // Assert
        var chart = card.FindControl<FlowChart>("TimelineChart")!;
        Assert.Same(data.Points, chart.Points);
        Assert.Same(data.Series, chart.Series);
        Assert.Equal("Nothing here", chart.Message);
    }

    // In the window

    private static string IntervalLabel(MainWindow window) => window.Find<TextBlock>("TrendIntervalLabel").Text ?? "";

    [AvaloniaFact]
    public async Task Window_WheelOverTheTrend_ZoomsTheIntervalOneStepAtATimeAndStopsAtTheEnds()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await workspace.OpenWindowAsync();
        var trend = window.Find<TrendCard>("TrendCard");
        window.Show();
        window.UpdateLayout();
        var point = Center(window, trend);
        var labels = new List<string> { IntervalLabel(window) };

        // Act
        window.MouseWheel(point, new Vector(0, 1));
        labels.Add(IntervalLabel(window));
        window.MouseWheel(point, new Vector(0, 1));
        labels.Add(IntervalLabel(window)); // already the coarsest
        foreach (var _ in new[] { 1, 2, 3 })
        {
            window.MouseWheel(point, new Vector(0, -1));
            labels.Add(IntervalLabel(window));
        }

        // Assert
        Assert.Equal([Get("Week"), Get("Month"), Get("Month"), Get("Week"), Get("Day"), Get("Day")], labels);
    }

    [AvaloniaFact]
    public async Task Window_Zoom_RedrawsTheTrendWithTheNewBuckets()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Coffee beans", LedgerSeed.Today);
        var window = await workspace.OpenWindowAsync();
        var chart = window.Find<FlowChart>("TrendChart");
        var weekly = chart.Points.Count;
        window.Show();
        window.UpdateLayout();

        // Act
        window.MouseWheel(Center(window, window.Find<TrendCard>("TrendCard")), new Vector(0, -1));

        // Assert
        Assert.True(chart.Points.Count > weekly);
    }

    [AvaloniaFact]
    public async Task Window_WithTransactions_DrawsBothChartsAndNoMessage()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Coffee beans", LedgerSeed.Today);

        // Act
        var window = await workspace.OpenWindowAsync();

        // Assert
        var trend = window.Find<FlowChart>("TrendChart");
        var timeline = window.Find<FlowChart>("TimelineChart");
        Assert.Equal(3, trend.Series.Count);
        Assert.Null(trend.Message);
        Assert.Equal(2, timeline.Series.Count);
        Assert.Null(timeline.Message);
        Assert.NotEmpty(trend.Points);
        Assert.NotEmpty(timeline.Points);
    }

    [AvaloniaFact]
    public async Task Window_WithoutTransactions_ShowsTheEmptyMessageInBothCharts()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();

        // Act
        var window = await workspace.OpenWindowAsync();

        // Assert
        Assert.Equal(Get("No matching transactions"), window.Find<FlowChart>("TrendChart").Message);
        Assert.Equal(Get("No matching transactions"), window.Find<FlowChart>("TimelineChart").Message);
    }
}
