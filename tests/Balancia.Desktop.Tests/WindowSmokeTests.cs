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

        // Act
        var window = await workspace.OpenWindowAsync();

        // Assert
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.Contains("Separate data folder", window.Title);
        Assert.True(window.Find<TextBlock>("TransactionsEmpty").IsVisible);
        Assert.Contains("0-0 / 0", window.Find<TextBlock>("TransactionsHeading").Text);
    }

    [AvaloniaFact]
    public async Task Open_WithSeededLedger_ListsAccountsAndTransactions()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var account = LedgerSeed.Account(store);
        LedgerSeed.Expense(store, account, "Coffee beans", LedgerSeed.Today, 12.50m);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, LedgerSeed.Today, "Salary", Money.FromFrancs(500m), account));

        // Act
        var window = await workspace.OpenWindowAsync();

        // Assert
        Assert.Equal(1, window.Find<ListBox>("AccountsList").ItemCount);
        Assert.Equal(2, window.Find<ListBox>("TransactionsList").ItemCount);
        Assert.False(window.Find<TextBlock>("TransactionsEmpty").IsVisible);
        Assert.Contains("1-2 / 2", window.Find<TextBlock>("TransactionsHeading").Text);
    }
}
