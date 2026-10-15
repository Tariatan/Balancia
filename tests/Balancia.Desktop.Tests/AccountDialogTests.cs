using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class AccountDialogTests
{
    private static async Task<MainWindow> OpenEmptyAsync(DesktopWorkspace workspace)
    {
        workspace.SeedStore();
        return await workspace.OpenWindowAsync();
    }

    [AvaloniaFact]
    public async Task Add_WithNameDateAndAmount_CreatesTheAccount()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenEmptyAsync(workspace);
        var dialog = await window.OpenDialogAsync("AccountAddButton", "Add account");
        dialog.Fill("Name", "Savings");
        dialog.Fill("Opening date", LedgerSeed.Today.AddDays(-10));
        dialog.Fill("Opening amount", "250.00");

        // Act
        await dialog.SaveAndCloseAsync(window);
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<ListBox>("AccountsList").ItemCount == 1, "the account to appear");

        // Assert
        var saved = Assert.Single(workspace.SeedStore().ReadSnapshot().Accounts);
        Assert.Equal("Savings", saved.Name);
        Assert.Equal(Money.FromFrancs(250m), saved.OpeningAmount);
        Assert.Equal(LedgerSeed.Today.AddDays(-10), saved.OpeningDate);
        Assert.False(window.Find<TextBlock>("AccountsEmpty").IsVisible);
    }

    [AvaloniaFact]
    public async Task Add_WithoutName_ShowsAnErrorAndCreatesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenEmptyAsync(workspace);
        var dialog = await window.OpenDialogAsync("AccountAddButton", "Add account");
        dialog.Fill("Name", "   ");

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal("Enter an account name.", error);
        Assert.Empty(workspace.SeedStore().ReadSnapshot().Accounts);
    }

    [AvaloniaFact]
    public async Task Add_WithOpeningDateInTheFuture_ShowsAnErrorAndCreatesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenEmptyAsync(workspace);
        var dialog = await window.OpenDialogAsync("AccountAddButton", "Add account");
        dialog.Fill("Name", "Future fund");
        dialog.Fill("Opening date", LedgerSeed.Today.AddDays(1));

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal("Opening date cannot be in the future.", error);
        Assert.Empty(workspace.SeedStore().ReadSnapshot().Accounts);
    }

    [AvaloniaFact]
    public async Task Edit_ByDoubleClickingTheRow_ShowsTheCurrentValuesAndSavesChanges()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var id = LedgerSeed.Account(store, "Everyday");
        var window = await workspace.OpenWindowAsync();
        var list = window.Find<ListBox>("AccountsList");
        list.SelectedIndex = 0;

        // Act
        window.DoubleClickRow(list, 0);
        var dialog = await window.WaitForDialogAsync("Edit account");
        var shownName = dialog.TextOf("Name");
        dialog.Fill("Name", "Daily spending");
        await dialog.SaveAndCloseAsync(window);

        // Assert
        Assert.Equal("Everyday", shownName);
        Assert.Equal("Daily spending", Assert.Single(store.ReadSnapshot().Accounts, account => account.Id == id).Name);
    }

    [AvaloniaFact]
    public async Task Archive_SelectedAccount_MarksItArchived()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var id = LedgerSeed.Account(store);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("AccountsList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("AccountArchiveButton", "Archive account");

        // Act
        await dialog.SaveAndCloseAsync(window, "Archive");

        // Assert
        Assert.True(Assert.Single(store.ReadSnapshot().Accounts, account => account.Id == id).Archived);
    }

    [AvaloniaFact]
    public async Task Delete_AccountWithoutTransactions_RemovesIt()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        LedgerSeed.Account(store);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("AccountsList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("AccountRemoveButton", "Delete account");

        // Act
        await dialog.SaveAndCloseAsync(window, "Delete");
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<TextBlock>("AccountsEmpty").IsVisible, "the account list to empty");

        // Assert
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [AvaloniaFact]
    public async Task Delete_AccountWithTransactions_IsRefusedAndKeepsTheAccount()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Lunch", LedgerSeed.Today);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("AccountsList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("AccountRemoveButton", "Delete account");

        // Act
        var error = await dialog.SaveExpectingErrorAsync("Delete");

        // Assert
        Assert.Equal("An account with transactions cannot be deleted. Archive it instead.", error);
        Assert.Single(store.ReadSnapshot().Accounts);
    }

    [AvaloniaFact]
    public async Task Delete_WithNothingSelected_AsksForASelectionInsteadOfOpeningADialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        LedgerSeed.Account(workspace.SeedStore());
        var window = await workspace.OpenWindowAsync();

        // Act
        window.Click("AccountRemoveButton");
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<TextBlock>("OverviewStatus").Text == "Select a row first.", "the prompt");

        // Assert
        Assert.Empty(window.OwnedWindows);
    }
}
