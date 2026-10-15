using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class KeyboardShortcutTests
{
    private static async Task<MainWindow> OpenWithOneTransactionAsync(DesktopWorkspace workspace)
    {
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Coffee beans", LedgerSeed.Today);
        var window = await workspace.OpenWindowAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task Delete_WithSelectedTransaction_RemovesItAfterConfirmation()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);
        window.Find<ListBox>("TransactionsList").SelectedIndex = 0;

        // Act
        window.PressKey(Key.Delete);
        var dialog = await window.WaitForDialogAsync("Remove transaction");
        await dialog.SaveAndCloseAsync(window, "Remove");
        await window.WaitForTransactionsAsync(0, "the transaction to be removed");

        // Assert
        Assert.True(window.Find<TextBlock>("TransactionsEmpty").IsVisible);
        Assert.Empty(workspace.SeedStore().ReadTransactions(new TransactionsFilter()).Hits);
    }

    [AvaloniaFact]
    public async Task Delete_WhenConfirmationIsCancelled_KeepsTheTransaction()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);
        window.Find<ListBox>("TransactionsList").SelectedIndex = 0;

        // Act
        window.PressKey(Key.Delete);
        var dialog = await window.WaitForDialogAsync("Remove transaction");
        await dialog.CancelAsync(window);

        // Assert
        Assert.Equal(1, window.TransactionCount());
    }

    [AvaloniaFact]
    public async Task Delete_WithoutSelection_OpensNoDialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);

        // Act
        window.PressKey(Key.Delete);
        await WindowDriver.SettleAsync();

        // Assert
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task Plus_OpensTheAddTransactionDialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);

        // Act
        window.PressKey(Key.OemPlus);
        var dialog = await window.WaitForDialogAsync("Add transaction");
        await dialog.CancelAsync(window);

        // Assert
        Assert.Equal(1, window.TransactionCount());
    }

    [AvaloniaFact]
    public async Task Plus_WithControlHeld_OpensNoDialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);

        // Act
        window.PressKey(Key.OemPlus, KeyModifiers.Control);
        await WindowDriver.SettleAsync();

        // Assert
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task Plus_WhileTypingInTheSearchBox_OpensNoDialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithOneTransactionAsync(workspace);

        // Act
        window.PressKey(Key.OemPlus, source: window.Find<TextBox>("FilterSearch"));
        await WindowDriver.SettleAsync();

        // Assert
        Assert.Empty(window.OwnedWindows);
    }
}
