using Xunit;

namespace Balancia.Core.Tests;

public class TransactionRulesTests
{
    private static readonly DateOnly Opened = new(2026, 1, 10);

    private static readonly Account Everyday = new("everyday", "Everyday", Opened, Money.Zero, false, Money.Zero);
    private static readonly Account Savings = new("savings", "Savings", new DateOnly(2026, 3, 1), Money.Zero, false, Money.Zero);
    private static readonly Account Closed = new("closed", "Closed", Opened, Money.Zero, true, Money.Zero);
    private static readonly Account[] Accounts = [Everyday, Savings, Closed];

    private static readonly Category Food = new("food", "Food", null, "Food", false);
    private static readonly Category Lunch = new("lunch", "Lunch", "food", "Food / Lunch", false);
    private static readonly Category Travel = new("travel", "Travel", null, "Travel", false);
    private static readonly Category Lunch2 = new("lunch2", "Lunch", "travel", "Travel / Lunch", false);
    private static readonly Category Old = new("old", "Old", null, "Old", true);
    private static readonly Category[] Categories = [Food, Lunch, Travel, Lunch2, Old];

    private static string? Check(string accountId = "everyday", string? destinationId = null, DateOnly? date = null, string? path = null,
        SavedTransaction? existing = null) =>
        TransactionRules.CheckSave(Accounts, Categories, date ?? Opened, accountId, destinationId, path, existing);

    [Fact]
    public void CheckSave_ActiveAccountAndNoCategory_IsAccepted() =>
        Assert.Null(Check());

    [Fact]
    public void CheckSave_UnknownAccount_IsRejected() =>
        Assert.Equal(TransactionRules.AccountMissingMessage, Check("gone"));

    [Fact]
    public void CheckSave_UnknownDestination_IsRejected() =>
        Assert.Equal(TransactionRules.AccountMissingMessage, Check(destinationId: "gone"));

    [Fact]
    public void CheckSave_DateBeforeTheOpeningDate_IsRejectedOnTheAccountAndOnTheDestination()
    {
        // Assert
        Assert.Null(Check(date: Opened));
        Assert.Equal(AccountRules.TransactionBeforeOpeningMessage, Check(date: Opened.AddDays(-1)));
        Assert.Equal(AccountRules.TransactionBeforeOpeningMessage, Check(destinationId: "savings", date: new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void CheckSave_ArchivedAccount_IsRejectedForANewTransaction() =>
        Assert.Equal(TransactionRules.AccountArchivedMessage, Check("closed"));

    [Fact]
    public void CheckSave_ArchivedDestination_IsRejectedForANewTransaction() =>
        Assert.Equal(TransactionRules.AccountArchivedMessage, Check(destinationId: "closed"));

    [Fact]
    public void CheckSave_ArchivedAccountThatTheEditedTransactionUses_IsAccepted()
    {
        // Arrange
        var existing = new SavedTransaction(["closed"], null);

        // Act and Assert
        Assert.Null(Check("closed", existing: existing));
        Assert.Null(Check(destinationId: "closed", existing: existing));
    }

    [Fact]
    public void CheckSave_ArchivedAccountThatTheEditedTransactionDoesNotUse_IsRejected() =>
        Assert.Equal(TransactionRules.AccountArchivedMessage, Check("closed", existing: new SavedTransaction(["everyday"], null)));

    [Fact]
    public void CheckSave_TheAccountIsCheckedBeforeTheCategory() =>
        Assert.Equal(TransactionRules.AccountMissingMessage, Check("gone", path: "Nonsense"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Food")]
    [InlineData("food")]
    [InlineData("Food / Lunch")]
    [InlineData(" Food / Lunch ")]
    [InlineData("Food/Lunch")] // any spacing around the separator and any letter case
    [InlineData("Food /Lunch")]
    [InlineData("Food/ Lunch")]
    [InlineData("Food  /  Lunch")]
    [InlineData("food/lunch")]
    [InlineData("FOOD/LUNCH")]
    public void CheckSave_NoCategoryOrAnActivePath_IsAccepted(string? path) =>
        Assert.Null(Check(path: path));

    [Theory]
    [InlineData("A / B / C")]
    [InlineData("Food /")]
    [InlineData("/ Lunch")]
    public void CheckSave_MalformedPath_ExplainsTheFormat(string path) =>
        Assert.Equal(CategoryRules.InvalidPathMessage, Check(path: path));

    [Theory]
    [InlineData("Missing")]
    [InlineData("Food / Dinner")]
    [InlineData("Lunch")] // a subcategory is never found without its parent
    public void CheckSave_UnknownPath_AsksToChooseAnExistingOne(string path) =>
        Assert.StartsWith("Choose an existing category path.", Check(path: path));

    [Theory]
    [InlineData("Missing", "Missing")]
    [InlineData("travel/ DINNER", "travel / DINNER")]
    [InlineData("  Food  /Dinner ", "Food / Dinner")]
    public void CheckSave_UnknownPath_ShowsThePathInTheSpellingTheLedgerUses(string path, string shown) =>
        Assert.Equal($"Choose an existing category path. No category \"{shown}\".", Check(path: path));

    [Fact]
    public void CheckSave_ArchivedCategory_AsksToRestoreIt() =>
        Assert.Equal(TransactionRules.PathArchivedMessage, Check(path: "Old"));

    [Fact]
    public void CheckSave_ArchivedCategoryThatTheEditedTransactionUses_IsAccepted() =>
        Assert.Null(Check(path: "Old", existing: new SavedTransaction([], "old")));

    [Fact]
    public void CheckSave_ArchivedCategoryThatTheEditedTransactionDoesNotUse_IsRejected() =>
        Assert.Equal(TransactionRules.PathArchivedMessage, Check(path: "Old", existing: new SavedTransaction([], "food")));

    [Theory]
    [InlineData("Food / Lunch", "lunch")]
    [InlineData("food/LUNCH", "lunch")]
    [InlineData("travel /lunch", "lunch2")]
    [InlineData("Travel/ Lunch", "lunch2")]
    [InlineData("Food", "food")]
    public void FindByPath_SubcategoriesWithTheSameNameUnderDifferentParents_AreToldApart(string path, string expectedId) =>
        Assert.Equal(expectedId, TransactionRules.FindByPath(Categories, path)?.Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A / B / C")]
    [InlineData("Food /")]
    [InlineData("Dinner")]
    public void FindByPath_NoPathOrNoSuchCategory_FindsNothing(string? path) =>
        Assert.Null(TransactionRules.FindByPath(Categories, path));

    [Fact]
    public void CheckCategoryId_UnknownCategory_IsRejected() =>
        Assert.Equal(TransactionRules.CategoryMissingMessage, TransactionRules.CheckCategoryId(Categories, "gone", null));

    [Fact]
    public void CheckCategoryId_ActiveCategory_IsAccepted() =>
        Assert.Null(TransactionRules.CheckCategoryId(Categories, "lunch", null));

    [Fact]
    public void CheckCategoryId_ArchivedCategory_IsRejectedUnlessTheEditedTransactionUsesIt()
    {
        // Assert
        Assert.Equal(TransactionRules.CategoryArchivedMessage, TransactionRules.CheckCategoryId(Categories, "old", null));
        Assert.Null(TransactionRules.CheckCategoryId(Categories, "old", new SavedTransaction([], "old")));
    }

    [Fact]
    public void SavedTransaction_From_CarriesTheAccountsAndTheCategory()
    {
        // Arrange
        var transfer = new TransactionDraft(TransactionKind.Transfer, Opened, "Move", Money.FromFrancs(1), "everyday", "savings");
        var expense = new TransactionDraft(TransactionKind.Expense, Opened, "Lunch", Money.FromFrancs(1), "everyday", CategoryId: "lunch");

        // Act
        var fromTransfer = SavedTransaction.From(transfer);
        var fromExpense = SavedTransaction.From(expense);

        // Assert
        Assert.Equal(["everyday", "savings"], fromTransfer.AccountIds);
        Assert.Equal(["everyday"], fromExpense.AccountIds);
        Assert.Equal("lunch", fromExpense.CategoryId);
    }
}
