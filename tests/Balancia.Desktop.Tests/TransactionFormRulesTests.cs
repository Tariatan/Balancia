using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class TransactionFormRulesTests
{
    private static readonly DateOnly Opened = new(2026, 1, 10);

    private static Account Account(string id, bool archived = false, DateOnly? opened = null) =>
        new(id, id.ToUpperInvariant(), opened ?? Opened, Money.Zero, archived, Money.Zero);

    private static Category Category(string id, string path, bool archived = false) =>
        new(id, path.Split('/').Last().Trim(), path.Contains('/') ? path.Split('/')[0].Trim().ToLowerInvariant() : null, path, archived);

    private static readonly Category Food = Category("food", "Food");
    private static readonly Category Lunch = Category("lunch", "Food / Lunch");
    private static readonly Category Old = Category("old", "Food / Old", archived: true);
    private static readonly Category[] All = [Food, Lunch, Old];
    private static readonly Category[] Available = [Food, Lunch];

    private static TransactionDraft Existing(string accountId, string? destinationId = null, string? categoryId = null) =>
        new(destinationId is null ? TransactionKind.Expense : TransactionKind.Transfer, Opened, "x", Money.FromFrancs(1), accountId, destinationId, categoryId);

    [Fact]
    public void AvailableAccounts_OffersActiveAccountsInTheirOrder()
    {
        // Act
        var accounts = TransactionFormRules.AvailableAccounts([Account("a"), Account("b", archived: true), Account("c")], null);

        // Assert
        Assert.Equal(["a", "c"], accounts.Select(a => a.Id));
    }

    [Fact]
    public void AvailableAccounts_KeepsAnArchivedAccountThatTheEditedEntryUses()
    {
        // Arrange
        var accounts = new[] { Account("a"), Account("b", archived: true), Account("c", archived: true), Account("d", archived: true) };

        // Act
        var offered = TransactionFormRules.AvailableAccounts(accounts, Existing("b", destinationId: "c"));

        // Assert
        Assert.Equal(["a", "b", "c"], offered.Select(a => a.Id));
    }

    [Fact]
    public void DefaultAccount_PrefersTheEditedEntryThenTheConfiguredDefaultThenTheLastUsed()
    {
        // Arrange
        var accounts = new[] { Account("a"), Account("b"), Account("c") };

        // Act and Assert
        Assert.Equal("c", TransactionFormRules.DefaultAccount(accounts, "c", "b", "a").Id);
        Assert.Equal("b", TransactionFormRules.DefaultAccount(accounts, null, "b", "a").Id);
        Assert.Equal("a", TransactionFormRules.DefaultAccount(accounts, null, null, "a").Id);
    }

    [Fact]
    public void DefaultAccount_NothingKnown_IsTheFirstAccount()
    {
        // Act
        var account = TransactionFormRules.DefaultAccount([Account("a"), Account("b")], null, null, null);

        // Assert
        Assert.Equal("a", account.Id);
    }

    [Fact]
    public void DefaultAccount_TheChosenIdIsNotOffered_FallsBackToTheFirstWithoutTryingTheLaterIds()
    {
        // Arrange
        // The configured default is archived and so not offered; the last used account is, but the chain stops at the first id that is set.
        var accounts = new[] { Account("a"), Account("b") };

        // Act
        var account = TransactionFormRules.DefaultAccount(accounts, null, "archived-default", "b");

        // Assert
        Assert.Equal("a", account.Id);
    }

    [Fact]
    public void AvailableCategories_OffersActiveCategoriesAndTheArchivedOneTheEditedEntryUses()
    {
        // Act and Assert
        Assert.Equal(["food", "lunch"], TransactionFormRules.AvailableCategories(All, null).Select(c => c.Id));
        Assert.Equal(["food", "lunch", "old"], TransactionFormRules.AvailableCategories(All, "old").Select(c => c.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadCategoryPath_Empty_IsNull(string? text)
    {
        // Act and Assert
        Assert.Null(TransactionFormRules.ReadCategoryPath(text, Available));
    }

    [Theory]
    [InlineData("food / lunch", "Food / Lunch")] // the stored spelling wins
    [InlineData("  FOOD  ", "Food")] // trimmed
    [InlineData("Travel", "Travel")] // unknown paths are kept as typed
    [InlineData("  Food/Lunch ", "Food / Lunch")] // any spacing around the separator gives the stored spelling
    [InlineData("food /LUNCH", "Food / Lunch")]
    [InlineData("travel/ Dinner", "travel / Dinner")] // an unknown path is tidied, its letter case kept
    [InlineData("Food/", "Food /")]
    public void ReadCategoryPath_Typed_GivesTheSpellingTheLedgerUses(string text, string expected)
    {
        // Act and Assert
        Assert.Equal(expected, TransactionFormRules.ReadCategoryPath(text, Available));
    }

    [Fact]
    public void CategoryPathFor_Transfer_IsAlwaysNone()
    {
        // Act and Assert
        Assert.Null(TransactionFormRules.CategoryPathFor(TransactionKind.Transfer, "Food", Available));
        Assert.Equal("Food", TransactionFormRules.CategoryPathFor(TransactionKind.Expense, "food", Available));
        Assert.Equal("Food", TransactionFormRules.CategoryPathFor(TransactionKind.Income, "food", Available));
    }

    [Fact]
    public void Validate_AllGood_IsAccepted()
    {
        // Act
        var error = TransactionFormRules.Validate(TransactionKind.Expense, Opened.AddDays(5), Account("a"), null, "Food / Lunch", All, null);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_DateOnTheOpeningDate_IsAcceptedAndTheDayBeforeIsNot()
    {
        // Act
        var onTheDay = TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a"), null, "Food / Lunch", All, null);
        var before = TransactionFormRules.Validate(TransactionKind.Expense, Opened.AddDays(-1), Account("a"), null, "Food / Lunch", All, null);

        // Assert
        Assert.Null(onTheDay);
        Assert.Equal("Transaction date precedes the account's opening date.", before);
    }

    [Fact]
    public void Validate_Transfer_ChecksTheDestinationOpeningDateToo()
    {
        // Arrange
        var source = Account("a", opened: new DateOnly(2026, 1, 1));
        var destination = Account("b", opened: new DateOnly(2026, 3, 1));

        // Act
        var error = TransactionFormRules.Validate(TransactionKind.Transfer, new DateOnly(2026, 2, 1), source, destination, null, All, null);

        // Assert
        Assert.Equal("Transaction date precedes the account's opening date.", error);
    }

    [Fact]
    public void Validate_NotATransfer_IgnoresTheDestination()
    {
        // Arrange
        var destination = Account("b", opened: new DateOnly(2026, 3, 1));

        // Act
        var error = TransactionFormRules.Validate(TransactionKind.Expense, new DateOnly(2026, 2, 1), Account("a"), destination, "Food / Lunch", All, null);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_Transfer_NeedsNoCategory()
    {
        // Act
        var error = TransactionFormRules.Validate(TransactionKind.Transfer, Opened, Account("a"), Account("b"), "Nonsense / A / B", All, null);

        // Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    [InlineData(TransactionKind.Income)]
    public void Validate_NotATransfer_RejectsAnUnknownCategory(TransactionKind kind)
    {
        // Act
        var error = TransactionFormRules.Validate(kind, Opened, Account("a"), null, "Travel", All, null);

        // Assert
        Assert.Equal("Choose an existing category path. No category \"Travel\".", error);
    }

    [Fact]
    public void Validate_TheDateIsCheckedBeforeTheCategory()
    {
        // Act
        var error = TransactionFormRules.Validate(TransactionKind.Expense, Opened.AddDays(-1), Account("a"), null, "Travel", All, null);

        // Assert
        Assert.Equal("Transaction date precedes the account's opening date.", error);
    }

    [Fact]
    public void Validate_ArchivedCategoryThatTheEditedEntryUses_IsAcceptedButNotForANewEntry()
    {
        // Act
        var editing = TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a"), null, "Food / Old", All, Existing("a", categoryId: "old"));
        var adding = TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a"), null, "Food / Old", All, null);

        // Assert
        Assert.Null(editing);
        Assert.Equal(TransactionRules.PathArchivedMessage, adding);
    }

    [Fact]
    public void Validate_ArchivedAccountThatTheEditedEntryUses_IsAcceptedButNotForANewEntry()
    {
        // Act
        var editing = TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a", archived: true), null, "Food / Lunch", All, Existing("a"));
        var adding = TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a", archived: true), null, "Food / Lunch", All, null);

        // Assert
        Assert.Null(editing);
        Assert.Equal(TransactionRules.AccountArchivedMessage, adding);
    }

    [Theory]
    [InlineData("Food/Lunch")]
    [InlineData("Food /Lunch")]
    [InlineData("Food/ Lunch")]
    [InlineData("food/lunch")]
    public void Validate_CategoryPathWithAnotherSeparatorSpelling_IsAccepted(string text) =>
        Assert.Null(TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a"), null, text, All, null));

    [Fact]
    public void Validate_UnknownCategory_ShowsThePathInTheLedgerSpelling() =>
        Assert.Equal("Choose an existing category path. No category \"food / Dinner\".",
            TransactionFormRules.Validate(TransactionKind.Expense, Opened, Account("a"), null, "food/Dinner", All, null));

    [Fact]
    public void ToDraft_Transfer_KeepsItsDestination()
    {
        // Act
        var draft = TransactionFormRules.ToDraft(TransactionKind.Transfer, Opened, "Move", Money.FromFrancs(5), "a", "b", "memo");

        // Assert
        Assert.Equal("b", draft.DestinationId);
        Assert.Equal(("Move", "memo", Money.FromFrancs(5), "a"), (draft.Description, draft.Memo, draft.Amount, draft.AccountId));
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    [InlineData(TransactionKind.Income)]
    public void ToDraft_NotATransfer_DropsAnyDestinationAndCarriesNoCategoryId(TransactionKind kind)
    {
        // Act
        var draft = TransactionFormRules.ToDraft(kind, Opened, "Lunch", Money.FromFrancs(5), "a", "b", "");

        // Assert
        Assert.Null(draft.DestinationId);
        Assert.Null(draft.CategoryId);
        Assert.Equal(kind, draft.Kind);
    }
}
