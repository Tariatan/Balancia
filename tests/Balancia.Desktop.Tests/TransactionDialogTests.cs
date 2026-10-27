using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Balancia.Core;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class TransactionDialogTests
{
    private const string CategoryLabel = "Category / subcategory";

    private static IReadOnlyList<LedgerEntry> SavedEntries(DesktopWorkspace workspace) =>
        workspace.SeedStore().ReadTransactions(new TransactionsFilter()).Hits.Select(hit => hit.Entry).ToArray();

    /// <summary>A ledger with one account and a Food category, opened in a window.</summary>
    private static async Task<MainWindow> OpenWithAccountAsync(DesktopWorkspace workspace)
    {
        var store = workspace.SeedStore();
        LedgerSeed.Account(store);
        store.SaveCategory(null, "Food", null);
        return await workspace.OpenWindowAsync();
    }

    [AvaloniaFact]
    public async Task Add_Expense_SavesAmountDescriptionAndCategory()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "12.50");
        dialog.Fill("Description", "Lunch");
        dialog.Fill(CategoryLabel, "Food");

        // Act
        await dialog.SaveAndCloseAsync(window);
        await window.WaitForTransactionsAsync(1, "the transaction to appear");

        // Assert
        var entry = Assert.Single(SavedEntries(workspace));
        Assert.Equal(TransactionKind.Expense, entry.Draft.Kind);
        Assert.Equal(Money.FromFrancs(12.50m), entry.Draft.Amount);
        Assert.Equal("Lunch", entry.Draft.Description);
        Assert.Equal("Food", entry.CategoryPath);
        Assert.Equal(LedgerSeed.Today, entry.Draft.Date);
    }

    [AvaloniaFact]
    public async Task Add_WithAnAmountExpression_SavesTheEvaluatedAmount()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "10+2.5");
        dialog.Fill("Description", "Split bill");

        // Act
        await dialog.SaveAndCloseAsync(window);

        // Assert
        Assert.Equal(Money.FromFrancs(12.50m), Assert.Single(SavedEntries(workspace)).Draft.Amount);
    }

    [AvaloniaFact]
    public async Task Add_Income_SavesItAsIncome()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Type", "Income");
        dialog.Fill("Amount (positive)", "500");
        dialog.Fill("Description", "Salary");

        // Act
        await dialog.SaveAndCloseAsync(window);

        // Assert
        var entry = Assert.Single(SavedEntries(workspace));
        Assert.Equal(TransactionKind.Income, entry.Draft.Kind);
        Assert.Equal(Money.FromFrancs(500m), entry.Draft.Amount);
    }

    [AvaloniaTheory]
    [InlineData("Amount (positive)", "", "Check the date (YYYY-MM-DD) and amount (for example 12.50).")]
    [InlineData(CategoryLabel, "Nonexistent", "Choose an existing category path.")]
    public async Task Add_WithAnInvalidValue_ShowsAnErrorAndSavesNothing(string label, string value, string expectedError)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "5");
        dialog.Fill(label, value);

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal(expectedError, error);
        Assert.Empty(SavedEntries(workspace));
    }

    [AvaloniaFact]
    public async Task Add_DatedBeforeTheAccountOpened_ShowsAnErrorAndSavesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "5");
        dialog.Fill("Date", LedgerSeed.Today.AddDays(-600));

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal("Transaction date precedes the account's opening date.", error);
        Assert.Empty(SavedEntries(workspace));
    }

    [AvaloniaFact]
    public async Task Add_Transfer_RecordsBothAccounts()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var everyday = LedgerSeed.Account(store, "Everyday");
        var savings = LedgerSeed.Account(store, "Savings");
        var window = await workspace.OpenWindowAsync();
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Type", "Transfer");
        dialog.Fill("Account", "Everyday");
        dialog.Fill("Destination account", "Savings");
        dialog.Fill("Amount (positive)", "40");

        // Act
        await dialog.SaveAndCloseAsync(window);

        // Assert
        var entry = Assert.Single(SavedEntries(workspace));
        Assert.Equal(TransactionKind.Transfer, entry.Draft.Kind);
        Assert.Equal(everyday, entry.Draft.AccountId);
        Assert.Equal(savings, entry.Draft.DestinationId);
        Assert.Null(entry.CategoryPath);
    }

    [AvaloniaFact]
    public async Task AddAnother_SavesAndClearsTheFormForTheNextEntry()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "5");
        dialog.Fill("Description", "Coffee");

        // Act
        dialog.ClickDialogButton("Add another transaction");
        // Idle again: cleared, fields re-enabled and no "Saving…" text. The dialog ignores Close while a save is still finishing.
        await DesktopWorkspace.WaitUntilAsync(
            () => dialog.TextOf("Amount (positive)") == "" && dialog.Field("Amount (positive)").IsEffectivelyEnabled && dialog.ErrorText() == "",
            "the form to clear and become editable again");

        // Assert
        Assert.Single(SavedEntries(workspace));
        Assert.Equal("", dialog.TextOf("Description"));
        Assert.Single(window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task AddAnother_ThenTypingTheStartOfTheSavedDescription_OffersItAgain()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "5");
        dialog.Fill("Description", "Coffee");
        dialog.ClickDialogButton("Add another transaction");
        await DesktopWorkspace.WaitUntilAsync(
            () => dialog.TextOf("Description") == "" && dialog.Field("Amount (positive)").IsEffectivelyEnabled && dialog.ErrorText() == "",
            "the form to clear and become editable again");
        var suggestions = dialog.GetLogicalDescendants().OfType<DescriptionSuggestBox>().Single();

        // Act
        dialog.Fill("Description", "Cof");

        // Assert
        await DesktopWorkspace.WaitUntilAsync(() => suggestions.Matches.Contains("Coffee"), "the saved description to be offered");
    }

    [AvaloniaFact]
    public async Task Cancel_DiscardsTheEnteredTransaction()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = await OpenWithAccountAsync(workspace);
        var dialog = await window.OpenDialogAsync("TransactionsAddButton", "Add transaction");
        dialog.Fill("Amount (positive)", "5");

        // Act
        await dialog.CancelAsync(window);

        // Assert
        Assert.Empty(SavedEntries(workspace));
    }

    [AvaloniaFact]
    public async Task Edit_ByDoubleClickingTheRow_ShowsTheCurrentValuesAndSavesChanges()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Lunch", LedgerSeed.Today, francs: 12.50m);
        var window = await workspace.OpenWindowAsync();
        var list = window.Find<ListBox>("TransactionsList");

        // Act
        window.DoubleClickRow(list, 0);
        var dialog = await window.WaitForDialogAsync("Edit transaction");
        var shownAmount = dialog.TextOf("Amount (positive)");
        var shownDescription = dialog.TextOf("Description");
        dialog.Fill("Amount (positive)", "15");
        dialog.Fill("Description", "Late lunch");
        await dialog.SaveAndCloseAsync(window);

        // Assert
        Assert.Equal("12.50", shownAmount);
        Assert.Equal("Lunch", shownDescription);
        var entry = Assert.Single(SavedEntries(workspace));
        Assert.Equal(Money.FromFrancs(15m), entry.Draft.Amount);
        Assert.Equal("Late lunch", entry.Draft.Description);
    }

    [AvaloniaFact]
    public async Task Add_WithoutAnyAccount_ExplainsInsteadOfOpeningADialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        var window = await workspace.OpenWindowAsync();

        // Act
        window.Click("TransactionsAddButton");
        await DesktopWorkspace.WaitUntilAsync(
            () => window.Find<TextBlock>("OverviewStatus").Text == "Add an active account before entering transactions.", "the explanation");

        // Assert
        Assert.Empty(window.OwnedWindows);
    }
}
