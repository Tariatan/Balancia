using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class LedgerStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-ledger-{Guid.NewGuid():N}.db");
    private readonly LedgerStore store;
    private readonly TestClock clock = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Start = new(2026, 9, 1);

    public LedgerStoreTests()
    {
        store = new LedgerStore(path, clock);
        store.Initialize();
    }

    private string Account(string name, decimal opening = 0) => store.SaveAccount(null, name, Start, Money.FromFrancs(opening));

    private static TransactionDraft Draft(string account, TransactionKind kind, decimal amount, string? to = null) =>
        new(kind, Start.AddDays(1), "Synthetic payment", Money.FromFrancs(amount), account, to);

    [Fact]
    public void Initialize_ReopenedLedger_PreservesDataAndExcludesOpeningsFromFlows()
    {
        // Arrange
        var a = Account("A", 1000);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 20));
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 50));
        var reopened = new LedgerStore(path, clock);
        reopened.Initialize();

        // Act
        var snapshot = reopened.ReadSnapshot();

        // Assert
        Assert.Equal(103000, snapshot.NetWorth.Centimes);
        Assert.Equal(5000, snapshot.MonthlyIncome.Centimes);
        Assert.Equal(2000, snapshot.MonthlyExpenses.Centimes);
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Equal(3, snapshot.Revision);
    }

    [Fact]
    public void SaveTransaction_NewTransfer_MovesAmountBetweenAccountsWithoutChangingFlows()
    {
        // Arrange
        var a = Account("A", 1000);
        var b = Account("B");

        // Act
        store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 100, b));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(90000, snapshot.Accounts.Single(x => x.Id == a).Balance.Centimes);
        Assert.Equal(10000, snapshot.Accounts.Single(x => x.Id == b).Balance.Centimes);
        Assert.Equal(100000, snapshot.NetWorth.Centimes);
        Assert.Equal(0, snapshot.MonthlyIncome.Centimes);
        Assert.Equal(0, snapshot.MonthlyExpenses.Centimes);
        Assert.Single(snapshot.Entries);
    }

    [Fact]
    public void SaveTransaction_EditedTransfer_RebalancesAllThreeAccounts()
    {
        // Arrange
        var a = Account("A", 1000);
        var b = Account("B");
        var c = Account("C");
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 100, b));

        // Act
        store.SaveTransaction(id, Draft(b, TransactionKind.Transfer, 75, c) with { Date = Start.AddDays(3) });

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(100000, snapshot.Accounts.Single(x => x.Id == a).Balance.Centimes);
        Assert.Equal(-7500, snapshot.Accounts.Single(x => x.Id == b).Balance.Centimes);
        Assert.Equal(7500, snapshot.Accounts.Single(x => x.Id == c).Balance.Centimes);
        Assert.Equal(Start.AddDays(3), snapshot.Entries.Single().Draft.Date);
    }

    [Fact]
    public void DeleteTransaction_Transfer_RestoresBalancesAndLeavesOnlyOpeningMovements()
    {
        // Arrange
        var a = Account("A", 1000);
        var b = Account("B");
        Account("C");
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 100, b));

        // Act
        store.DeleteTransaction(id);

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Empty(snapshot.Entries);
        Assert.Equal(100000, snapshot.NetWorth.Centimes);
        Assert.Equal(3L, SqliteScript.Scalar(path, "SELECT COUNT(*) FROM movements"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveTransaction_FailureMidTransfer_RollsBackCreationOrEdit(bool edit)
    {
        // Arrange
        var a = Account("A", 1000);
        var b = Account("B");
        var id = edit ? store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 10, b)) : null;
        var before = store.ReadSnapshot();
        SqliteScript.Execute(path, "CREATE TRIGGER fail_second BEFORE INSERT ON movements WHEN NEW.amount>0 AND NEW.transaction_id NOT LIKE 'opening:%' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");

        // Act
        void Act() => store.SaveTransaction(id, Draft(a, TransactionKind.Transfer, 100, b));

        // Assert
        Assert.Throws<SqliteException>(Act);
        var after = store.ReadSnapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Accounts.ToArray(), after.Accounts.ToArray());
        Assert.Equal(before.Entries.ToArray(), after.Entries.ToArray());
    }

    [Fact]
    public void DeleteTransaction_FailureMidTransfer_RollsBackBothSides()
    {
        // Arrange
        var a = Account("A", 1000);
        var b = Account("B");
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 10, b));
        var before = store.ReadSnapshot();
        SqliteScript.Execute(path, "CREATE TRIGGER fail_delete BEFORE DELETE ON movements WHEN OLD.amount>0 BEGIN SELECT RAISE(ABORT,'injected failure'); END;");

        // Act
        void Act() => store.DeleteTransaction(id);

        // Assert
        Assert.Throws<SqliteException>(Act);
        var after = store.ReadSnapshot();
        Assert.Equal(before.Entries.ToArray(), after.Entries.ToArray());
        Assert.Equal(before.Revision, after.Revision);
    }

    [Fact]
    public void SaveTransaction_DecimalFrancs_SumsExactly()
    {
        // Arrange
        var a = Account("A");

        // Act
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, .1m));
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, .2m));

        // Assert
        Assert.Equal(30, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void SaveTransaction_ZeroAmount_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A");

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, 0));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveTransaction_NegativeAmount_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A");

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, -1));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveTransaction_TransferToSameAccount_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A");

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 1, a));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveTransaction_FutureDate_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A");

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { Date = new DateOnly(2027, 1, 1) });

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveAccount_EditedOpening_DoesNotDoubleCountExistingHistory()
    {
        // Arrange
        var a = Account("A", 100);
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 10));

        // Act
        store.SaveAccount(a, "Renamed", Start.AddDays(-1), Money.FromFrancs(200));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(21000, snapshot.NetWorth.Centimes);
        Assert.Equal(1000, snapshot.MonthlyIncome.Centimes);
    }

    [Fact]
    public void SaveAccount_OpeningDateAfterFirstTransaction_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A", 100);
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 10));

        // Act
        void Act() => store.SaveAccount(a, "A", Start.AddDays(3), new Money(0));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveTransaction_DateBeforeAccountOpening_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A", 100);

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { Date = Start.AddDays(-2) });

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void DeleteTransaction_OpeningBalanceId_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A", 100);

        // Act
        void Act() => store.DeleteTransaction("opening:" + a);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveCategory_ParentRename_PreservesAssignmentsAndPaths()
    {
        // Arrange
        var a = Account("A");
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 20) with { CategoryId = child });

        // Act
        store.SaveCategory(parent, "Meals", null);

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal("Meals / Lunch", snapshot.Entries.Single().CategoryPath);
        Assert.Equal("Meals", snapshot.LargestCategories.Single().Name);
    }

    [Fact]
    public void SaveCategory_ThirdLevel_ThrowsArgumentException()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);

        // Act
        void Act() => store.SaveCategory(null, "Third", child);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveCategory_ParentUnderOwnChild_ThrowsArgumentException()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);

        // Act
        void Act() => store.SaveCategory(parent, "Cycle", child);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void SaveCategory_ArchivedParent_ArchivesChildrenAndRejectsNewAssignmentsButKeepsExisting()
    {
        // Arrange
        var a = Account("A");
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 20) with { CategoryId = child });

        // Act
        store.SaveCategory(parent, "Food", null, true);
        void AssignNew() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { CategoryId = child });
        store.SaveTransaction(id, Draft(a, TransactionKind.Expense, 25) with { CategoryId = child });

        // Assert
        Assert.All(store.ReadSnapshot().Categories, c => Assert.True(c.Archived));
        Assert.Throws<ArgumentException>(AssignNew);
        Assert.Equal(2500, store.ReadSnapshot().MonthlyExpenses.Centimes);
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_ExistingPath_AssignsCaseInsensitivelyWithoutNewCategories()
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        SeedTravelCategories();

        // Act
        store.SaveTransactionWithCategoryPath(null, draft, "Travel / Rail");
        store.SaveTransactionWithCategoryPath(null, draft, "travel / rail");

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(4, snapshot.Categories.Count);
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.All(snapshot.Entries, entry => Assert.Equal("Travel / Rail", entry.CategoryPath));
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_EditingEntry_ReassignsCategory()
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        SeedTravelCategories();
        var first = store.SaveTransactionWithCategoryPath(null, draft, "Travel / Rail");

        // Act
        store.SaveTransactionWithCategoryPath(first, draft, "Travel / Taxi");

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal("Travel / Taxi", snapshot.Entries.Single(entry => entry.Id == first).CategoryPath);
        Assert.Equal(4, snapshot.Categories.Count);
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_BlankPath_SavesUncategorizedEntry()
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        SeedTravelCategories();

        // Act
        store.SaveTransactionWithCategoryPath(null, draft, "Parking");
        store.SaveTransactionWithCategoryPath(null, draft, " ");

        // Assert
        Assert.Contains(store.ReadSnapshot().Entries, entry => entry.CategoryPath is null);
    }

    [Theory]
    [InlineData("Unmatched free-text category")]
    [InlineData("Travel / Missing")]
    public void SaveTransactionWithCategoryPath_UnknownPath_ThrowsAndKeepsRevision(string categoryPath)
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        SeedTravelCategories();
        var revision = store.ReadSnapshot().Revision;

        // Act
        void Act() => store.SaveTransactionWithCategoryPath(null, draft, categoryPath);

        // Assert
        Assert.Throws<ArgumentException>(Act);
        Assert.Equal(revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_MissingAccount_RollsBackWithoutCreatingCategories()
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        var revision = store.ReadSnapshot().Revision;

        // Act
        void Act() => store.SaveTransactionWithCategoryPath(null, draft with { AccountId = "missing-account" }, "New / Subcategory");

        // Assert
        Assert.Throws<ArgumentException>(Act);
        var after = store.ReadSnapshot();
        Assert.Equal(revision, after.Revision);
        Assert.DoesNotContain(after.Categories, category => category.Name == "New");
        Assert.DoesNotContain(after.Entries, entry => entry.CategoryPath == "New / Subcategory");
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_FailureInsertingMovement_RollsBack()
    {
        // Arrange
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        SeedTravelCategories();
        var revision = store.ReadSnapshot().Revision;
        SqliteScript.Execute(path, "CREATE TRIGGER fail_typed_path BEFORE INSERT ON movements WHEN NEW.transaction_id NOT LIKE 'opening:%' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");

        // Act
        void Act() => store.SaveTransactionWithCategoryPath(null, draft, "Travel / Rail");

        // Assert
        Assert.Throws<SqliteException>(Act);
        var after = store.ReadSnapshot();
        Assert.Equal(revision, after.Revision);
        Assert.Equal(4, after.Categories.Count);
    }

    [Fact]
    public void SaveTransactionWithCategoryPath_ArchivedCategoryOnExistingEntry_KeepsAssignment()
    {
        // Arrange
        var account = Account("Synthetic account");
        var draft = Draft(account, TransactionKind.Expense, 10);
        var parentId = store.SaveCategory(null, "Home", null);
        var childId = store.SaveCategory(null, "Utilities", parentId);
        var entryId = store.SaveTransaction(null, draft with { CategoryId = childId });
        store.SaveCategory(parentId, "Home", null, true);

        // Act
        store.SaveTransactionWithCategoryPath(entryId, draft with { Amount = Money.FromFrancs(11) }, "Home / Utilities");

        // Assert
        Assert.Equal("Home / Utilities", store.ReadSnapshot().Entries.Single().CategoryPath);
    }

    [Theory]
    [InlineData("Home / Utilities")]
    [InlineData("Home / Other")]
    public void SaveTransactionWithCategoryPath_ArchivedCategoryOnNewEntry_ThrowsWithoutCreatingCategory(string categoryPath)
    {
        // Arrange
        var account = Account("Synthetic account");
        var draft = Draft(account, TransactionKind.Expense, 10);
        var parentId = store.SaveCategory(null, "Home", null);
        store.SaveCategory(null, "Utilities", parentId);
        store.SaveCategory(parentId, "Home", null, true);

        // Act
        void Act() => store.SaveTransactionWithCategoryPath(null, draft, categoryPath);

        // Assert
        Assert.Throws<ArgumentException>(Act);
        Assert.DoesNotContain(store.ReadSnapshot().Categories, category => category.Name == "Other");
    }

    [Fact]
    public void SaveTransaction_ArchivedAccount_KeepsBalanceAndAllowsEditingExistingEntry()
    {
        // Arrange
        var a = Account("A", 100);
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 10));
        store.SaveAccount(a, "A", Start, Money.FromFrancs(100), true);

        // Act
        store.SaveTransaction(id, Draft(a, TransactionKind.Expense, 20));

        // Assert
        Assert.Equal(8000, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void SaveTransaction_NewEntryOnArchivedAccount_ThrowsArgumentException()
    {
        // Arrange
        var a = Account("A", 100);
        store.SaveAccount(a, "A", Start, Money.FromFrancs(100), true);

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, 1));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void DeleteAccount_NoTransactions_RemovesAccountAndOpeningBalance()
    {
        // Arrange
        var a = Account("A", 100);

        // Act
        store.DeleteAccount(a);

        // Assert
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void DeleteAccount_HasOwnTransaction_ThrowsAndKeepsAccountAndTransaction()
    {
        // Arrange
        var a = Account("A", 100);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 10));

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => store.DeleteAccount(a));

        // Assert
        Assert.Contains("cannot be deleted", error.Message);
        var snapshot = store.ReadSnapshot();
        Assert.Single(snapshot.Accounts);
        Assert.Single(snapshot.Entries);
    }

    [Fact]
    public void DeleteAccount_UsedAsTransferDestination_ThrowsAndKeepsBothAccounts()
    {
        // Arrange
        var a = Account("A", 100);
        var b = Account("B", 0);
        store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 20, b));

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => store.DeleteAccount(b));

        // Assert
        Assert.Contains("cannot be deleted", error.Message);
        Assert.Equal(2, store.ReadSnapshot().Accounts.Count);
    }

    [Fact]
    public void DeleteAccount_UnknownId_ThrowsWithoutChangingLedger()
    {
        // Arrange
        var before = store.ReadSnapshot();

        // Act
        void Act() => store.DeleteAccount(Guid.NewGuid().ToString("N"));

        // Assert
        Assert.Throws<ArgumentException>(Act);
        Assert.Equal(before.Revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void ReadRegularCategoryPaths_TransactionDates_UsesInclusive35DayWindow()
    {
        // Arrange
        clock.Now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var account = Account("A");
        var outside = store.SaveCategory(null, "Outside", null);
        var boundary = store.SaveCategory(null, "Boundary", null);
        var today = store.SaveCategory(null, "Today", null);
        var future = store.SaveCategory(null, "Future", null);
        store.SaveCategory(null, "Unused", null);
        var draft = Draft(account, TransactionKind.Expense, 1);
        store.SaveTransaction(null, draft with { Date = new(2026, 9, 1), CategoryId = outside });
        store.SaveTransaction(null, draft with { Date = new(2026, 9, 2), CategoryId = boundary });
        store.SaveTransaction(null, draft with { Date = new(2026, 10, 6), CategoryId = today });
        store.SaveTransaction(null, draft with { Date = new(2026, 10, 7), CategoryId = future });
        clock.Now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var revision = store.ReadSnapshot().Revision;

        // Act
        var paths = store.ReadRegularCategoryPaths();

        // Assert
        Assert.Equal(["Today", "Boundary"], paths);
        Assert.Equal(revision, store.ReadSnapshot().Revision);
        clock.Now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(["Future", "Today"], store.ReadRegularCategoryPaths());
    }

    [Fact]
    public void ReadRegularCategoryPaths_FrequencyTies_OrdersByCountThenLatestDateThenName()
    {
        // Arrange
        var account = Account("A");
        var alpha = store.SaveCategory(null, "Alpha", null);
        var beta = store.SaveCategory(null, "Beta", null);
        var most = store.SaveCategory(null, "Most used", null);
        var latest = store.SaveCategory(null, "Latest", null);
        var draft = Draft(account, TransactionKind.Expense, 1);
        string[] categoryIds = [alpha, beta, most, most];
        foreach (var id in categoryIds)
        {
            store.SaveTransaction(null, draft with { CategoryId = id });
        }
        store.SaveTransaction(null, draft with { Date = Start.AddDays(5), CategoryId = latest });

        // Act
        var paths = store.ReadRegularCategoryPaths();

        // Assert
        Assert.Equal(["Most used", "Latest", "Alpha", "Beta"], paths);
    }

    [Fact]
    public void ReadRegularCategoryPaths_EditDeleteArchiveAndRestart_RecomputesFromLedger()
    {
        // Arrange
        var account = Account("A");
        var shop = store.SaveCategory(null, "Shop", null);
        var misc = store.SaveCategory(null, "Misc", shop);
        var draft = Draft(account, TransactionKind.Expense, 1) with { CategoryId = misc };
        var transaction = store.SaveTransaction(null, draft);
        store.SaveCategory(shop, "Shopping", null);
        var reopened = new LedgerStore(path, clock);
        reopened.Initialize();

        // Act / Assert
        Assert.Equal(["Shopping / Misc"], reopened.ReadRegularCategoryPaths());
        reopened.SaveCategory(misc, "Misc", shop, archived: true);
        Assert.Empty(reopened.ReadRegularCategoryPaths());
        reopened.SaveCategory(misc, "Misc", shop);
        Assert.Equal(["Shopping / Misc"], reopened.ReadRegularCategoryPaths());
        reopened.SaveTransaction(transaction, draft with { CategoryId = null });
        Assert.Empty(reopened.ReadRegularCategoryPaths());
        reopened.SaveTransaction(transaction, draft);
        Assert.Equal(["Shopping / Misc"], reopened.ReadRegularCategoryPaths());
        reopened.DeleteTransaction(transaction);
        Assert.Empty(reopened.ReadRegularCategoryPaths());
    }

    [Fact]
    public void ReadRegularCategoryPaths_SaveAndContinueData_FiltersBeforeChoosingTwo()
    {
        // Arrange
        var account = Account("A");
        var shop = store.SaveCategory(null, "Shop", null);
        var shopMisc = store.SaveCategory(null, "Misc", shop);
        var misc = store.SaveCategory(null, "Misc", null);
        var miscMisc = store.SaveCategory(null, "Misc", misc);
        var other = store.SaveCategory(null, "Popular", null);
        var draft = Draft(account, TransactionKind.Expense, 1);
        store.SaveTransaction(null, draft with { CategoryId = other });
        store.SaveTransaction(null, draft with { CategoryId = other });
        store.SaveTransaction(null, draft with { CategoryId = shopMisc });
        string[] paths = ["Shop / Misc", "Misc / Misc", "Popular"];
        var before = CategorySuggestionRanking.Build(paths, store.ReadRegularCategoryPaths(), "misc");

        // Act
        store.SaveTransactionWithCategoryPath(null, draft, "Misc / Misc");
        var after = CategorySuggestionRanking.Build(paths, store.ReadRegularCategoryPaths(), "misc");

        // Assert
        Assert.Equal(CategorySuggestionSection.Regular, before[0].Section);
        Assert.Equal("Shop / Misc", before[0].Path);
        Assert.Equal(["Misc / Misc", "Shop / Misc"], after.Select(suggestion => suggestion.Path));
        Assert.Equal(CategorySuggestionSection.Regular, after[0].Section);
        Assert.Null(after[1].Section);
    }

    [Fact]
    public void SaveTransaction_IncomeOverflowingNetWorth_ThrowsAndKeepsRevision()
    {
        // Arrange
        var a = store.SaveAccount(null, "A", Start, new Money(long.MaxValue));
        var before = store.ReadSnapshot();

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, .01m));

        // Assert
        Assert.Throws<OverflowException>(Act);
        var after = store.ReadSnapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(long.MaxValue, after.NetWorth.Centimes);
    }

    [Fact]
    public void SaveTransaction_MonthlyExpensesExactlyAtLimit_SucceedsAndNextCentimeOverflows()
    {
        // Arrange
        var a = store.SaveAccount(null, "A", Start, new Money(long.MaxValue));
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 46116860184273879.04m));
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 46116860184273879.03m));
        var before = store.ReadSnapshot();

        // Act
        void Act() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, .01m));

        // Assert
        Assert.Equal(long.MaxValue, before.MonthlyExpenses.Centimes);
        Assert.Equal(0, before.NetWorth.Centimes);
        Assert.Throws<OverflowException>(Act);
        Assert.Equal(before.Revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void ReadSnapshot_ClockMovesToNextMonth_ResetsMonthlyIncomeButKeepsNetWorth()
    {
        // Arrange
        var a = Account("A");
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 50));

        // Act
        clock.Now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var snapshot = store.ReadSnapshot();

        // Assert
        Assert.Equal(0, snapshot.MonthlyIncome.Centimes);
        Assert.Equal(5000, snapshot.NetWorth.Centimes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Initialize_UnsupportedSchemaVersion_ThrowsWithoutModification(int version)
    {
        // Arrange
        SqliteScript.Execute(path, $"PRAGMA user_version={version};");

        // Act
        void Act() => store.Initialize();

        // Assert
        Assert.Throws<InvalidOperationException>(Act);
        Assert.Equal((long)version, SqliteScript.Scalar(path, "PRAGMA user_version"));
    }

    [Fact]
    public void Initialize_NonEmptyUnversionedFile_ThrowsWithoutModification()
    {
        // Arrange
        var foreignPath = path + ".foreign";
        try
        {
            SqliteScript.Execute(foreignPath, "CREATE TABLE foreign_table(id INTEGER)");
            var foreignStore = new LedgerStore(foreignPath, clock);

            // Act
            void Act() => foreignStore.Initialize();

            // Assert
            Assert.Throws<InvalidOperationException>(Act);
            Assert.Equal(0L, Convert.ToInt64(SqliteScript.Scalar(foreignPath, "SELECT COUNT(*) FROM sqlite_master WHERE name='accounts'")));
        }
        finally
        {
            File.Delete(foreignPath);
        }
    }

    [Fact]
    public void SaveTransaction_DescriptionWithSurroundingSpaces_StoresTheTrimmedText()
    {
        // Arrange
        var a = Account("A", 100);
        var draft = Draft(a, TransactionKind.Expense, 5) with { Description = "  Rent \t" };

        // Act
        store.SaveTransaction(null, draft);

        // Assert
        Assert.Equal("Rent", store.ReadSnapshot().Entries.Single().Draft.Description);
    }

    [Fact]
    public void DeleteCategory_Unused_RemovesIt()
    {
        // Arrange
        var id = store.SaveCategory(null, "Spare", null);

        // Act
        store.DeleteCategory(id);

        // Assert
        Assert.DoesNotContain(store.ReadSnapshot().Categories, category => category.Id == id);
    }

    [Fact]
    public void DeleteCategory_WithSubcategories_ThrowsAndKeepsBoth()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Travel", null);
        store.SaveCategory(null, "Rail", parent);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => store.DeleteCategory(parent));

        // Assert
        Assert.Contains("subcategories", error.Message);
        Assert.Equal(2, store.ReadSnapshot().Categories.Count);
    }

    [Fact]
    public void DeleteCategory_UsedByTransactions_ThrowsAndKeepsIt()
    {
        // Arrange
        var a = Account("A", 100);
        var category = store.SaveCategory(null, "Food", null);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 5) with { CategoryId = category });

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => store.DeleteCategory(category));

        // Assert
        Assert.Contains("used by transactions", error.Message);
        Assert.Single(store.ReadSnapshot().Categories);
    }

    [Fact]
    public void DeleteCategory_UnknownId_Throws()
    {
        // Arrange
        var unknown = Guid.NewGuid().ToString("N");

        // Act
        void Act() => store.DeleteCategory(unknown);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Theory]
    [InlineData("Food")]
    [InlineData("food")]
    [InlineData("  FOOD ")]
    public void SaveCategory_NameAlreadyUsedUnderTheSameParent_Throws(string name)
    {
        // Arrange
        store.SaveCategory(null, "Food", null);

        // Act
        var error = Assert.Throws<ArgumentException>(() => store.SaveCategory(null, name, null));

        // Assert
        Assert.Equal(CategoryRules.DuplicateNameMessage, error.Message);
        Assert.Single(store.ReadSnapshot().Categories);
    }

    [Fact]
    public void SaveCategory_SameNameUnderAnotherParent_IsAllowed()
    {
        // Arrange
        var travel = store.SaveCategory(null, "Travel", null);
        var home = store.SaveCategory(null, "Home", null);
        store.SaveCategory(null, "Other", travel);

        // Act
        store.SaveCategory(null, "Other", home);

        // Assert
        Assert.Equal(4, store.ReadSnapshot().Categories.Count);
    }

    [Fact]
    public void SaveCategory_ResavingWithItsOwnName_Succeeds()
    {
        // Arrange
        var id = store.SaveCategory(null, "Food", null);

        // Act
        store.SaveCategory(id, "Food", null, archived: true);

        // Assert
        Assert.True(store.ReadSnapshot().Categories.Single().Archived);
    }

    [Fact]
    public void ReadSnapshot_CategoryWithTransactions_FlagsOnlyThatCategory()
    {
        // Arrange
        var a = Account("A", 100);
        var used = store.SaveCategory(null, "Food", null);
        store.SaveCategory(null, "Spare", null);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 5) with { CategoryId = used });

        // Act
        var categories = store.ReadSnapshot().Categories;

        // Assert
        Assert.Equal(["Food"], categories.Where(category => category.HasTransactions).Select(category => category.Name));
    }

    [Theory]
    [InlineData("Everyday")]
    [InlineData("everyday")]
    [InlineData("  EVERYDAY ")]
    public void SaveAccount_NameAlreadyUsedByAnotherAccount_Throws(string name)
    {
        // Arrange
        Account("Everyday");

        // Act
        var error = Assert.Throws<ArgumentException>(() => store.SaveAccount(null, name, Start, Money.Zero));

        // Assert
        Assert.Equal(AccountRules.DuplicateNameMessage, error.Message);
        Assert.Single(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void SaveAccount_ResavingWithItsOwnName_Succeeds()
    {
        // Arrange
        var id = Account("Everyday");

        // Act
        store.SaveAccount(id, "EVERYDAY", Start, Money.Zero, archived: true);

        // Assert
        Assert.True(store.ReadSnapshot().Accounts.Single().Archived);
    }

    [Fact]
    public void ReadSnapshot_Accounts_CarryTheDateOfTheirFirstEntryOtherThanTheOpeningBalance()
    {
        // Arrange
        var a = Account("A", 100);
        var b = Account("B");
        var unused = Account("Unused");
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 5) with { Date = Start.AddDays(9) });
        store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 5, b) with { Date = Start.AddDays(4) });

        // Act
        var accounts = store.ReadSnapshot().Accounts.ToDictionary(account => account.Id);

        // Assert
        Assert.Equal(Start.AddDays(4), accounts[a].FirstTransactionDate);
        Assert.Equal(Start.AddDays(4), accounts[b].FirstTransactionDate);
        Assert.Null(accounts[unused].FirstTransactionDate);
    }

    [Fact]
    public void SaveReminder_DescriptionOfAnotherActiveReminder_ThrowsTheRuleMessage()
    {
        // Arrange
        store.SaveReminder(null, "Rent", Start, Money.FromFrancs(5m), 1);

        // Act
        var error = Assert.Throws<ArgumentException>(() => store.SaveReminder(null, " Rent ", Start, Money.FromFrancs(5m), 1));

        // Assert
        Assert.Equal(ReminderRules.DuplicateDescriptionMessage, error.Message);
    }

    private void SeedTravelCategories()
    {
        var parent = store.SaveCategory(null, "Travel", null);
        store.SaveCategory(null, "Rail", parent);
        store.SaveCategory(null, "Taxi", parent);
        store.SaveCategory(null, "Parking", null);
    }

    public void Dispose() => File.Delete(path);
}
