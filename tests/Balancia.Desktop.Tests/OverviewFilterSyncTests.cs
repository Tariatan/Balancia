using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Xunit;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

/// <summary>The controls that show the overview state stay in line with it after each filter action, through the real window.</summary>
public sealed class OverviewFilterSyncTests
{
    private static readonly Color SelectedPeriodColor = Color.Parse("#D8ECF3");

    private static string IntervalLabel(MainWindow window) => window.Find<TextBlock>("TrendIntervalLabel").Text ?? "";

    private static bool IsHighlighted(MainWindow window, string buttonName) =>
        window.Find<Button>(buttonName).Background is ISolidColorBrush { Color: var color } && color == SelectedPeriodColor;

    private static DateTime? Picked(MainWindow window, string pickerName) => window.Find<CalendarDatePicker>(pickerName).SelectedDate;

    private static async Task EnterCustomRangeAsync(MainWindow window)
    {
        window.Find<CalendarDatePicker>("FilterFrom").SelectedDate = LedgerSeed.Today.AddDays(-3).ToDateTime(TimeOnly.MinValue);
        window.Find<CalendarDatePicker>("FilterTo").SelectedDate = LedgerSeed.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        window.EnterText("FilterSearch", string.Empty);
        await DesktopWorkspace.WaitUntilAsync(() => IntervalLabel(window) == Get("Day"), "the custom range to apply");
    }

    [AvaloniaFact]
    public async Task ClearAll_FromACustomRange_ReturnsToThisMonthWithItsZoomButtonsAndDates()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await EnterCustomRangeAsync(window);
        Assert.False(IsHighlighted(window, "FilterButtonMonth"));

        // Act
        window.Click("FilterClearAll");
        await DesktopWorkspace.WaitUntilAsync(() => IntervalLabel(window) == Get("Week"), "the trend zoom to return to this month's");

        // Assert
        Assert.True(IsHighlighted(window, "FilterButtonMonth"));
        Assert.Equal(new DateTime(LedgerSeed.Today.Year, LedgerSeed.Today.Month, 1), Picked(window, "FilterFrom"));
    }

    [AvaloniaFact]
    public async Task PeriodButton_Year_ShowsTheYearsZoomButtonAndDates()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();

        // Act
        window.Click("FilterButtonYear");
        await DesktopWorkspace.WaitUntilAsync(() => IntervalLabel(window) == Get("Month"), "the year's zoom to show");

        // Assert
        Assert.True(IsHighlighted(window, "FilterButtonYear"));
        Assert.False(IsHighlighted(window, "FilterButtonMonth"));
        Assert.Equal(new DateTime(LedgerSeed.Today.Year, 1, 1), Picked(window, "FilterFrom"));
        Assert.Equal(new DateTime(LedgerSeed.Today.Year, 12, 31), Picked(window, "FilterTo"));
    }

    [AvaloniaFact]
    public async Task TypedDates_DifferentFromThePeriod_ShowACustomRangeWithNoHighlightedPeriod()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        Assert.True(IsHighlighted(window, "FilterButtonMonth"));

        // Act
        await EnterCustomRangeAsync(window);

        // Assert
        foreach (var button in new[] { "FilterButtonAll", "FilterButtonToday", "FilterButtonWeek", "FilterButtonMonth", "FilterButtonYear", "FilterButtonLast30Days" })
        {
            Assert.False(IsHighlighted(window, button), button);
        }
    }

    [AvaloniaFact]
    public async Task CategoryCheck_OnAParent_ChecksItsSubcategories()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var food = store.SaveCategory(null, "Food", null);
        store.SaveCategory(null, "Lunch", food);
        var window = await workspace.OpenWindowAsync();
        window.Show();
        window.UpdateLayout();
        var checks = window.Find<ListBox>("CategoriesList").GetLogicalDescendants().OfType<CheckBox>().ToArray();
        var parent = checks.Single(check => AutomationProperties.GetName(check) == "Food");
        var child = checks.Single(check => AutomationProperties.GetName(check) == "Food / Lunch");

        // Act
        parent.IsChecked = true;
        await DesktopWorkspace.WaitUntilAsync(() => child.IsChecked == true, "the subcategory to be checked");

        // Assert
        Assert.True(parent.IsChecked);
        Assert.True(child.IsChecked);
    }
}
