using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class PaginationTests
{
    private const int TransactionCount = 150;

    private static async Task<MainWindow> OpenWithManyTransactionsAsync(DesktopWorkspace workspace)
    {
        var store = workspace.SeedStore();
        var account = LedgerSeed.Account(store);
        for (var index = 1; index <= TransactionCount; index++)
        {
            LedgerSeed.Expense(store, account, $"Purchase {index:000}", LedgerSeed.Today);
        }

        var window = workspace.CreateSeparateFolderWindow();
        await DesktopWorkspace.OpenAsync(window);
        return window;
    }

    [AvaloniaFact]
    public async Task Open_WithMoreThanOnePageOfTransactions_ShowsTheFirstHundred()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();

        // Act
        var window = await OpenWithManyTransactionsAsync(workspace);

        // Assert
        Assert.Equal(100, window.TransactionCount());
        Assert.Contains("1-100 / 150", window.TransactionsHeading());
        Assert.False(window.Find<Button>("TransactionsPreviousPage").IsEnabled);
        Assert.True(window.Find<Button>("TransactionsNextPage").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task NextPage_FromTheFirstPage_ShowsTheRemainingFifty()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithManyTransactionsAsync(workspace);

        // Act
        window.Click("TransactionsNextPage");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 50, "the second page");

        // Assert
        Assert.Contains("101-150 / 150", window.TransactionsHeading());
        Assert.True(window.Find<Button>("TransactionsPreviousPage").IsEnabled);
        Assert.False(window.Find<Button>("TransactionsNextPage").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PreviousPage_FromTheSecondPage_ReturnsToTheFirstHundred()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithManyTransactionsAsync(workspace);
        window.Click("TransactionsNextPage");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 50, "the second page");

        // Act
        window.Click("TransactionsPreviousPage");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 100, "the first page");

        // Assert
        Assert.Contains("1-100 / 150", window.TransactionsHeading());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Search_FromTheSecondPage_ReturnsToTheFirstPage()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithManyTransactionsAsync(workspace);
        window.Click("TransactionsNextPage");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 50, "the second page");

        // Act
        window.EnterText("FilterSearch", "Purchase 12");
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 10, "the search to apply");

        // Assert
        Assert.Contains("1-10 / 10", window.TransactionsHeading());
        window.Close();
    }
}
