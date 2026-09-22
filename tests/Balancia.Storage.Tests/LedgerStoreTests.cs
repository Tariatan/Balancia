using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class LedgerStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-ledger-{Guid.NewGuid():N}.db");
    private readonly LedgerStore store;
    private readonly FixedClock clock = new();
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
    public void L01_OpeningsAreExcludedFromFlowsAndReopenPreservesData()
    {
        var a = Account("A", 1000);
        store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 20));
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 50));
        var reopened = new LedgerStore(path, clock);
        reopened.Initialize();
        var s = reopened.ReadSnapshot();
        Assert.Equal(103000, s.NetWorth.Centimes);
        Assert.Equal(5000, s.MonthlyIncome.Centimes);
        Assert.Equal(2000, s.MonthlyExpenses.Centimes);
        Assert.Equal(2, s.Entries.Count);
        Assert.Equal(3, s.Revision);
    }

    [Fact]
    public void L02_L03_TransferCreateEditAndDeleteRemainBalanced()
    {
        var a = Account("A", 1000);
        var b = Account("B");
        var c = Account("C");
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 100, b));
        var s = store.ReadSnapshot();
        Assert.Equal(90000, s.Accounts.Single(x => x.Id == a).Balance.Centimes);
        Assert.Equal(10000, s.Accounts.Single(x => x.Id == b).Balance.Centimes);
        Assert.Equal(100000, s.NetWorth.Centimes);
        Assert.Equal(0, s.MonthlyIncome.Centimes);
        Assert.Equal(0, s.MonthlyExpenses.Centimes);
        Assert.Single(s.Entries);
        store.SaveTransaction(id, Draft(b, TransactionKind.Transfer, 75, c) with
        {
            Date = Start.AddDays(3)
        });
        s = store.ReadSnapshot();
        Assert.Equal(100000, s.Accounts.Single(x => x.Id == a).Balance.Centimes);
        Assert.Equal(-7500, s.Accounts.Single(x => x.Id == b).Balance.Centimes);
        Assert.Equal(7500, s.Accounts.Single(x => x.Id == c).Balance.Centimes);
        Assert.Equal(Start.AddDays(3), s.Entries.Single().Draft.Date);
        store.DeleteTransaction(id);
        s = store.ReadSnapshot();
        Assert.Empty(s.Entries);
        Assert.Equal(100000, s.NetWorth.Centimes);
        Assert.Equal(3L, Scalar("SELECT COUNT(*) FROM movements")); // Only openings remain.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void L04_MidTransferFailureRollsBackCreationOrEdit(bool edit)
    {
        var a = Account("A", 1000);
        var b = Account("B");
        var id = edit ? store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 10, b)) : null;
        var before = store.ReadSnapshot();
        Sql("CREATE TRIGGER fail_second BEFORE INSERT ON movements WHEN NEW.amount>0 AND NEW.transaction_id NOT LIKE 'opening:%' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
        Assert.Throws<SqliteException>(() => store.SaveTransaction(id, Draft(a, TransactionKind.Transfer, 100, b)));
        var after = store.ReadSnapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Accounts.ToArray(), after.Accounts.ToArray());
        Assert.Equal(before.Entries.ToArray(), after.Entries.ToArray());
    }

    [Fact]
    public void FailedDeleteRollsBackBothSides()
    {
        var a = Account("A", 1000);
        var b = Account("B");
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 10, b));
        var before = store.ReadSnapshot();
        Sql("CREATE TRIGGER fail_delete BEFORE DELETE ON movements WHEN OLD.amount>0 BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
        Assert.Throws<SqliteException>(() => store.DeleteTransaction(id));
        Assert.Equal(before.Entries.ToArray(), store.ReadSnapshot().Entries.ToArray());
        Assert.Equal(before.Revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void L05_ExactMoneyAndValidation()
    {
        var a = Account("A");
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, .1m));
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, .2m));
        Assert.Equal(30, store.ReadSnapshot().NetWorth.Centimes);
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, 0)));
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, -1)));
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Transfer, 1, a)));
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { Date = new DateOnly(2027, 1, 1) }));
    }

    [Fact]
    public void L06_OpeningEditCannotDoubleCountOrMovePastHistory()
    {
        var a = Account("A", 100);
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 10));
        store.SaveAccount(a, "Renamed", Start.AddDays(-1), Money.FromFrancs(200));
        Assert.Equal(21000, store.ReadSnapshot().NetWorth.Centimes);
        Assert.Equal(1000, store.ReadSnapshot().MonthlyIncome.Centimes);
        Assert.Throws<ArgumentException>(() => store.SaveAccount(a, "A", Start.AddDays(3), new Money(0)));
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { Date = Start.AddDays(-2) }));
        Assert.Throws<ArgumentException>(() => store.DeleteTransaction("opening:" + a));
    }

    [Fact]
    public void CategoriesRemainTwoLevelsAndRenamesPreserveAssignments()
    {
        var a = Account("A");
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 20) with
        {
            CategoryId = child
        });
        store.SaveCategory(parent, "Meals", null);
        Assert.Equal("Meals / Lunch", store.ReadSnapshot().Entries.Single().CategoryPath);
        Assert.Equal("Meals", store.ReadSnapshot().LargestCategories.Single().Name);
        Assert.Throws<ArgumentException>(() => store.SaveCategory(null, "Third", child));
        Assert.Throws<ArgumentException>(() => store.SaveCategory(parent, "Cycle", child));
        store.SaveCategory(parent, "Meals", null, true);
        Assert.All(store.ReadSnapshot().Categories, c => Assert.True(c.Archived));
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 1) with { CategoryId = child }));
        store.SaveTransaction(id, Draft(a, TransactionKind.Expense, 25) with
        {
            CategoryId = child
        });
        Assert.Equal(2500, store.ReadSnapshot().MonthlyExpenses.Centimes);
    }

    [Fact]
    public void TypedCategoryPathCreatesOrReusesCategoriesWithTransactionAtomically()
    {
        var account = Account("Synthetic account", 100);
        var draft = Draft(account, TransactionKind.Expense, 10);
        var first = store.SaveTransactionWithCategoryPath(null, draft, "Travel / Rail");
        var initial = store.ReadSnapshot();
        Assert.Equal("Travel / Rail", initial.Entries.Single().CategoryPath);
        Assert.Equal(2, initial.Categories.Count);

        store.SaveTransactionWithCategoryPath(null, draft, "travel / rail");
        var reused = store.ReadSnapshot();
        Assert.Equal(2, reused.Categories.Count);
        Assert.Equal(2, reused.Entries.Count);

        store.SaveTransactionWithCategoryPath(first, draft, "Travel / Taxi");
        var edited = store.ReadSnapshot();
        Assert.Equal("Travel / Taxi", edited.Entries.Single(entry => entry.Id == first).CategoryPath);
        Assert.Equal(3, edited.Categories.Count);

        store.SaveTransactionWithCategoryPath(null, draft, "Parking");
        store.SaveTransactionWithCategoryPath(null, draft, " ");
        var withTopLevelAndUncategorized = store.ReadSnapshot();
        Assert.Contains(withTopLevelAndUncategorized.Categories, category => category.Path == "Parking");
        Assert.Contains(withTopLevelAndUncategorized.Entries, entry => entry.CategoryPath is null);

        var beforeFailure = withTopLevelAndUncategorized.Revision;
        Assert.Throws<ArgumentException>(() => store.SaveTransactionWithCategoryPath(null,
            draft with
            {
                AccountId = "missing-account"
            }, "New / Subcategory"));
        var afterFailure = store.ReadSnapshot();
        Assert.Equal(beforeFailure, afterFailure.Revision);
        Assert.DoesNotContain(afterFailure.Categories, category => category.Name == "New");
        Assert.DoesNotContain(afterFailure.Entries, entry => entry.CategoryPath == "New / Subcategory");

        Sql("CREATE TRIGGER fail_typed_path BEFORE INSERT ON movements WHEN NEW.transaction_id NOT LIKE 'opening:%' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
        Assert.Throws<SqliteException>(() => store.SaveTransactionWithCategoryPath(null, draft, "Failed / Child"));
        var afterInjectedFailure = store.ReadSnapshot();
        Assert.Equal(beforeFailure, afterInjectedFailure.Revision);
        Assert.DoesNotContain(afterInjectedFailure.Categories, category => category.Name == "Failed");

        Assert.Throws<ArgumentException>(() => store.SaveTransactionWithCategoryPath(null, draft, "Bad / / Path"));
        Assert.Equal(beforeFailure, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void TypedCategoryPathPreservesExistingArchivedAssignmentButRejectsNewUse()
    {
        var account = Account("Synthetic account");
        var draft = Draft(account, TransactionKind.Expense, 10);
        var entryId = store.SaveTransactionWithCategoryPath(null, draft, "Home / Utilities");
        var parent = store.ReadSnapshot().Categories.Single(category => category.Name == "Home");
        store.SaveCategory(parent.Id, parent.Name, null, true);

        store.SaveTransactionWithCategoryPath(entryId, draft with
        {
            Amount = Money.FromFrancs(11)
        }, "Home / Utilities");
        Assert.Throws<ArgumentException>(() => store.SaveTransactionWithCategoryPath(null, draft, "Home / Utilities"));
        Assert.Throws<ArgumentException>(() => store.SaveTransactionWithCategoryPath(null, draft, "Home / Other"));
        Assert.DoesNotContain(store.ReadSnapshot().Categories, category => category.Name == "Other");
    }

    [Fact]
    public void ArchivedAccountsKeepBalancesButRejectNewMovements()
    {
        var a = Account("A", 100);
        var id = store.SaveTransaction(null, Draft(a, TransactionKind.Expense, 10));
        store.SaveAccount(a, "A", Start, Money.FromFrancs(100), true);
        Assert.Equal(9000, store.ReadSnapshot().NetWorth.Centimes);
        Assert.Throws<ArgumentException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, 1)));
        store.SaveTransaction(id, Draft(a, TransactionKind.Expense, 20));
        Assert.Equal(8000, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void ReadRecentCategoryPaths_MultipleTransactions_ReturnsDistinctPathsByLatestTransactionDate()
    {
        // Arrange
        var account = Account("A");
        var draft = Draft(account, TransactionKind.Expense, 10);
        store.SaveTransactionWithCategoryPath(null, draft with
        {
            Date = Start.AddDays(1)
        }, "Food / Restaurant");
        store.SaveTransactionWithCategoryPath(null, draft with
        {
            Date = Start.AddDays(4)
        }, "Car / Fuel");
        store.SaveTransactionWithCategoryPath(null, draft with
        {
            Date = Start.AddDays(3)
        }, "Food / Restaurant");
        store.SaveTransactionWithCategoryPath(null, draft with
        {
            Date = Start.AddDays(2)
        }, "Health");

        // Act
        var paths = store.ReadRecentCategoryPaths(2);

        // Assert
        Assert.Equal(["Car / Fuel", "Food / Restaurant"], paths);
    }

    [Fact]
    public void OverflowAbortsWriteAndRevision()
    {
        var a = store.SaveAccount(null, "A", Start, new Money(long.MaxValue));
        var before = store.ReadSnapshot();
        Assert.Throws<OverflowException>(() => store.SaveTransaction(null, Draft(a, TransactionKind.Income, .01m)));
        Assert.Equal(before.Revision, store.ReadSnapshot().Revision);
        Assert.Equal(long.MaxValue, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void ClockControlsCurrentMonthTotals()
    {
        var a = Account("A");
        store.SaveTransaction(null, Draft(a, TransactionKind.Income, 50));
        clock.Now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, store.ReadSnapshot().MonthlyIncome.Centimes);
        Assert.Equal(5000, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void NewerSchemaIsRejectedWithoutModification()
    {
        Sql("PRAGMA user_version=4;");
        Assert.Throws<InvalidOperationException>(() => store.Initialize());
        Assert.Equal(4L, Scalar("PRAGMA user_version"));
    }

    private object? Scalar(string sql)
    {
        using var c = new SqliteConnectionFactory(path).Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
    private void Sql(string sql)
    {
        using var c = new SqliteConnectionFactory(path).Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    public void Dispose() => File.Delete(path);
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
