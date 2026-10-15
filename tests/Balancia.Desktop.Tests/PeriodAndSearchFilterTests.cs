using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class PeriodAndSearchFilterTests
{
    [AvaloniaFact]
    public async Task Open_WithTransactionsFromThreeEras_StartsOnThisMonth()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = workspace.CreateSeparateFolderWindow();

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Equal(1, window.TransactionCount());
    }

    [AvaloniaTheory]
    [InlineData("FilterButtonToday")]
    [InlineData("FilterButtonWeek")]
    [InlineData("FilterButtonMonth")]
    [InlineData("FilterButtonLast30Days")]
    public async Task PeriodButton_AfterShowingAll_NarrowsToTheCurrentPeriod(string buttonName)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await window.ShowAllTransactionsAsync(3);

        // Act
        window.Click(buttonName);
        await window.WaitForTransactionsAsync(1, "the period to narrow the list");

        // Assert
        Assert.Contains("1-1 / 1", window.TransactionsHeading());
    }

    [AvaloniaFact]
    public async Task YearButton_ExcludesTransactionsFromTheYearBeforeLast()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await window.ShowAllTransactionsAsync(3);

        // Act
        window.Click("FilterButtonYear");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() < 3, "the year filter to apply");

        // Assert
        Assert.InRange(window.TransactionCount(), 1, 2);
    }

    [AvaloniaFact]
    public async Task Search_WithDescriptionText_ListsOnlyMatchingTransactions()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await window.ShowAllTransactionsAsync(3);

        // Act
        window.EnterText("FilterSearch", "COFFEE");
        await window.WaitForTransactionsAsync(2, "the search to apply");

        // Assert
        Assert.Contains("1-2 / 2", window.TransactionsHeading());
    }

    [AvaloniaFact]
    public async Task Search_WithoutMatches_ShowsTheEmptyMessage()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();

        // Act
        window.EnterText("FilterSearch", "no such description");
        await window.WaitForTransactionsAsync(0, "the search to apply");

        // Assert
        Assert.True(window.Find<TextBlock>("TransactionsEmpty").IsVisible);
        Assert.Contains("0-0 / 0", window.TransactionsHeading());
    }

    [AvaloniaFact]
    public async Task ClearSearchButton_AfterSearching_RestoresTheFullList()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await window.ShowAllTransactionsAsync(3);
        window.EnterText("FilterSearch", "coffee");
        await window.WaitForTransactionsAsync(2, "the search to apply");

        // Act
        window.Click("FilterClearSearch");
        await window.WaitForTransactionsAsync(3, "the search to clear");

        // Assert
        Assert.Equal("", window.Find<TextBox>("FilterSearch").Text);
    }

    [AvaloniaFact]
    public async Task ClearAllButton_AfterSearching_ResetsTheSearchBox()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.ThreeEras(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();
        await window.ShowAllTransactionsAsync(3);
        window.EnterText("FilterSearch", "coffee");
        await window.WaitForTransactionsAsync(2, "the search to apply");

        // Act
        window.Click("FilterClearAll");
        await window.WaitForTransactionsAsync(3, "the filters to clear");

        // Assert
        Assert.Equal("", window.Find<TextBox>("FilterSearch").Text);
    }
}
