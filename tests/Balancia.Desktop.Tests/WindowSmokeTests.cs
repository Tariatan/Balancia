using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class WindowSmokeTests
{
    [AvaloniaFact]
    public async Task Open_WithSeparateDataFolder_ShowsEmptyLedgerAndCreatesDatabase()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = workspace.CreateSeparateFolderWindow();

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.Contains("Separate data folder", window.Title);
        Assert.True(window.FindControl<TextBlock>("TransactionsEmpty")!.IsVisible);
        Assert.Contains("0-0 / 0", window.FindControl<TextBlock>("TransactionsHeading")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Open_WithSeededLedger_ListsAccountsAndTransactions()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var account = store.SaveAccount(null, "Everyday", today.AddDays(-30), Money.FromFrancs(100m));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Coffee beans", Money.FromFrancs(12.50m), account));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, today, "Salary", Money.FromFrancs(500m), account));
        var window = workspace.CreateSeparateFolderWindow();

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Equal(1, window.FindControl<ListBox>("AccountsList")!.ItemCount);
        Assert.Equal(2, window.FindControl<ListBox>("TransactionsList")!.ItemCount);
        Assert.False(window.FindControl<TextBlock>("TransactionsEmpty")!.IsVisible);
        Assert.Contains("1-2 / 2", window.FindControl<TextBlock>("TransactionsHeading")!.Text);
        window.Close();
    }
}
