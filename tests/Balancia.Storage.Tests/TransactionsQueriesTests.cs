using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class TransactionsQueriesTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "balancia-transactions-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly TestClock clock = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly LedgerStore store;
    private static readonly DateOnly Start = new(2026, 9, 1);

    public TransactionsQueriesTests()
    {
        store = new LedgerStore(path, clock);
        store.Initialize();
    }

    private string Account(string name) => store.SaveAccount(null, name, Start, new Money(0));

    private string Entry(string account, string text, decimal francs, string? category = null, DateOnly? date = null,
        TransactionKind kind = TransactionKind.Expense) => store.SaveTransaction(null,
        new TransactionDraft(kind, date ?? Start.AddDays(1), text, Money.FromFrancs(francs), account, CategoryId: category));

    [Fact]
    public void ReadTransactions_CombinedFilters_MatchesOnlyInclusiveEndpointsCaseInsensitively()
    {
        // Arrange
        var a = Account("A");
        var b = Account("B");
        var match = Entry(a, "Lunch At WORK", 12.50m);
        Entry(a, "Lunch at home", 12.50m, date: Start.AddDays(2));
        Entry(b, "Lunch at work", 12.50m);
        Entry(a, "Lunch at work", 13m);
        Entry(a, "Lunch at work", 12.50m, kind: TransactionKind.Income);
        Entry(a, "Crème brûlée", 2m);
        var filter = new TransactionsFilter("lUnCh aT wOrK", a, TransactionKind.Expense, null,
            Start.AddDays(1), Start.AddDays(1), Money.FromFrancs(12.50m), Money.FromFrancs(12.50m));

        // Act
        var page = store.ReadTransactions(filter);

        // Assert
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match, page.Hits.Single().Entry.Id);
        Assert.Equal(-1250, page.Hits.Single().AccountEffect?.Centimes);
    }

    [Fact]
    public void ReadTransactions_AccentedDescriptionInDifferentCase_Matches()
    {
        // Arrange
        var a = Account("A");
        Entry(a, "Crème brûlée", 2m);

        // Act
        var page = store.ReadTransactions(new TransactionsFilter(Description: "CRÈME"));

        // Assert
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public void ReadTransactions_ParentCategoryFilter_IncludesOwnAndChildEntries()
    {
        // Arrange
        var a = Account("A");
        var food = store.SaveCategory(null, "Food", null);
        var lunch = store.SaveCategory(null, "Lunch", food);
        var car = store.SaveCategory(null, "Car", null);
        Entry(a, "one", 1, food);
        Entry(a, "two", 2, lunch);
        Entry(a, "three", 3, car);

        // Act
        var parentCount = store.ReadTransactions(new TransactionsFilter(CategoryId: food)).TotalCount;
        var childCount = store.ReadTransactions(new TransactionsFilter(CategoryId: lunch)).TotalCount;

        // Assert
        Assert.Equal(2, parentCount);
        Assert.Equal(1, childCount);
    }

    [Fact]
    public void ReadTransactionsAfter_SameDayEntriesAndTransfer_PagesWithoutGapsOrDuplicates()
    {
        // Arrange
        var a = Account("A");
        var b = Account("B");
        for (var i = 0; i < 29; i++)
        {
            Entry(a, "same day " + i, 1);
        }

        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, Start.AddDays(1), "Move", Money.FromFrancs(5), a, b));
        var all = new List<string>();

        // Act
        var page = store.ReadTransactions(new TransactionsFilter(), 0, 7);
        while (page.Hits.Count > 0)
        {
            all.AddRange(page.Hits.Select(h => h.Entry.Id));
            var last = page.Hits[^1].Entry;
            page = store.ReadTransactionsAfter(new TransactionsFilter(), last.Draft.Date, last.Id, 7);
        }

        // Assert
        Assert.Equal(30, all.Count);
        Assert.Equal(30, all.Distinct().Count());
        Assert.Equal(store.ReadSnapshot().Entries.Select(e => e.Id), all);
        Assert.Equal(9, store.FindTransactionOffset(all[9]));
    }

    [Fact]
    public void ReadTransactions_TransferDestinationAccount_ReportsPositiveAccountEffect()
    {
        // Arrange
        var a = Account("A");
        var b = Account("B");
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, Start.AddDays(1), "Move", Money.FromFrancs(5), a, b));

        // Act
        var transfer = store.ReadTransactions(new TransactionsFilter(AccountId: b)).Hits.Single();

        // Assert
        Assert.Equal(500, transfer.AccountEffect?.Centimes);
    }

    [Fact]
    public void ReadAllTransactions_MoreThanOnePageInPeriod_ReturnsEveryTransactionInPeriod()
    {
        // Arrange
        var account = Account("Everyday");
        Entry(account, "outside", 1, date: Start.AddDays(1));
        for (var i = 0; i < 1005; i++)
        {
            Entry(account, "inside " + i, 1, date: Start.AddDays(2));
        }

        // Act
        var page = store.ReadAllTransactions(new TransactionsFilter(From: Start.AddDays(2), To: Start.AddDays(2)));

        // Assert
        Assert.Equal(1005, page.TotalCount);
        Assert.Equal(1005, page.Hits.Count);
        Assert.Equal(1005, page.Hits.Select(hit => hit.Entry.Id).Distinct().Count());
        Assert.DoesNotContain(page.Hits, hit => hit.Entry.Draft.Description == "outside");
    }

    [Fact]
    public void SaveTransaction_AmountEdit_RefreshesDesktopAndHistoryTotals()
    {
        // Arrange
        var a = Account("A");
        var id = Entry(a, "Expense", 10);
        var original = store.ReadTransactions(new TransactionsFilter()).Hits.Single().Entry.Draft;

        // Act
        store.SaveTransaction(id, original with { Amount = Money.FromFrancs(15) });

        // Assert
        Assert.Equal(1500, store.ReadDesktopSnapshot().MonthlyExpenses.Centimes);
        Assert.Equal(1500, store.ReadTransactions(new TransactionsFilter()).Hits.Single().Entry.Draft.Amount.Centimes);
    }

    [Fact]
    public void ReadDesktopSnapshot_ClockMovesToNextMonth_ResetsMonthlyExpenses()
    {
        // Arrange
        var a = Account("A");
        Entry(a, "Expense", 10);

        // Act
        clock.Now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        // Assert
        Assert.Equal(0, store.ReadDesktopSnapshot().MonthlyExpenses.Centimes);
    }

    [Fact]
    public void DeleteTransaction_OnlyExpense_EmptiesHistoryAndNetWorth()
    {
        // Arrange
        var a = Account("A");
        var id = Entry(a, "Expense", 10);

        // Act
        store.DeleteTransaction(id);

        // Assert
        Assert.Empty(store.ReadTransactions(new TransactionsFilter()).Hits);
        Assert.Equal(0, store.ReadDesktopSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void ReadDesktopSnapshotForPeriod_DifferentPeriods_ChangesFlowsAndCategoriesButNotBalances()
    {
        // Arrange
        var account = store.SaveAccount(null, "Everyday", new DateOnly(2026, 8, 1), new Money(0));
        var food = store.SaveCategory(null, "Food", null);
        var transport = store.SaveCategory(null, "Transport", null);
        Entry(account, "August groceries", 10, food, new DateOnly(2026, 8, 31));
        Entry(account, "September train", 20, transport, Start);
        Entry(account, "September salary", 50, date: Start.AddDays(17), kind: TransactionKind.Income);

        // Act
        var august = store.ReadDesktopSnapshotForPeriod(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var september = store.ReadDesktopSnapshotForPeriod(Start, Start.AddDays(17));
        var all = store.ReadDesktopSnapshotForPeriod(null, null);

        // Assert
        Assert.Equal(2000, august.NetWorth.Centimes);
        Assert.Equal(2000, september.NetWorth.Centimes);
        Assert.Equal(2000, all.NetWorth.Centimes);
        Assert.Equal(1000, august.MonthlyExpenses.Centimes);
        Assert.Equal(0, august.MonthlyIncome.Centimes);
        Assert.Equal("Food", august.LargestCategories.Single().Name);
        Assert.Equal(2000, september.MonthlyExpenses.Centimes);
        Assert.Equal(5000, september.MonthlyIncome.Centimes);
        Assert.Equal("Transport", september.LargestCategories.Single().Name);
        Assert.Equal(3000, all.MonthlyExpenses.Centimes);
        Assert.Equal(2, all.LargestCategories.Count);
    }

    [Fact]
    public void ReadTransactions_SingleDayPeriod_ReturnsOnlyThatDay()
    {
        // Arrange
        var account = store.SaveAccount(null, "Everyday", new DateOnly(2026, 8, 1), new Money(0));
        Entry(account, "August groceries", 10, date: new DateOnly(2026, 8, 31));
        Entry(account, "September train", 20, date: Start);

        // Act
        var count = store.ReadTransactions(new TransactionsFilter(From: new DateOnly(2026, 8, 31), To: new DateOnly(2026, 8, 31))).TotalCount;

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void ReadDesktopSnapshotForPeriod_EndBeforeStart_ThrowsArgumentException()
    {
        // Arrange
        var to = new DateOnly(2026, 8, 31);

        // Act
        void Act() => store.ReadDesktopSnapshotForPeriod(Start, to);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void ReadDesktopSnapshotForFilter_ParentCategoryAndSharedFilter_MatchesHistoryAndBreaksDownParent()
    {
        // Arrange
        var filter = SeedOverviewScenario(out _);

        // Act
        var history = store.ReadTransactions(filter);
        var overview = store.ReadDesktopSnapshotForFilter(filter);

        // Assert
        Assert.Equal(4, history.TotalCount);
        Assert.Equal(1200, overview.MonthlyExpenses.Centimes);
        Assert.Equal(700, overview.MonthlyIncome.Centimes);
        Assert.Equal(["Dinner", "Lunch", "Food"], overview.LargestCategories.Select(x => x.Name));
        Assert.Equal([500, 400, 300], overview.LargestCategories.Select(x => x.Amount.Centimes));
        Assert.Equal(store.ReadDesktopSnapshotForPeriod(null, null).NetWorth, overview.NetWorth);
    }

    [Fact]
    public void ReadDesktopSnapshotForFilter_ExpenseKind_ExcludesIncome()
    {
        // Arrange
        var filter = SeedOverviewScenario(out _);

        // Act
        var expensesOnly = store.ReadDesktopSnapshotForFilter(filter with { Kind = TransactionKind.Expense });

        // Assert
        Assert.Equal(0, expensesOnly.MonthlyIncome.Centimes);
        Assert.Equal(1200, expensesOnly.MonthlyExpenses.Centimes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadDesktopSnapshotForFilter_SelectedSubcategories_BreaksDownChildrenRegardlessOfParentSelection(bool selectParents)
    {
        // Arrange
        var filter = SeedOverviewScenario(out var lunch);
        var food = filter.CategoryId!;
        var dinner = store.ReadDesktopSnapshot().Categories.Single(category => category.Name == "Dinner").Id;
        var home = store.SaveCategory(null, "Home", null);
        var equipment = store.SaveCategory(null, "Equipment", home);
        var otherLunch = store.SaveCategory(null, "Lunch", home);
        Entry(filter.AccountId!, "match", 6, equipment);
        Entry(filter.AccountId!, "match", 3.50m, otherLunch);
        var selected = new List<string> { lunch, dinner, equipment, otherLunch };
        if (selectParents)
        {
            selected.AddRange([food, home]);
        }

        // Act
        var overview = store.ReadDesktopSnapshotForFilter(filter with { CategoryId = null, CategoryIds = selected });

        // Assert
        string[] expectedNames = selectParents
            ? ["Equipment", "Dinner", "Lunch", "Lunch", "Food"]
            : ["Equipment", "Dinner", "Lunch", "Lunch"];
        long[] expectedAmounts = selectParents ? [600, 500, 400, 350, 300] : [600, 500, 400, 350];
        Assert.Equal(expectedNames, overview.LargestCategories.Select(category => category.Name));
        Assert.Equal(expectedAmounts, overview.LargestCategories.Select(category => category.Amount.Centimes));
        Assert.Equal(expectedAmounts.Sum(), overview.MonthlyExpenses.Centimes);
        Assert.Equal(700, overview.MonthlyIncome.Centimes);
    }

    [Fact]
    public void ReadDesktopSnapshotForFilter_ChildCategory_RestrictsTotalsToChild()
    {
        // Arrange
        var filter = SeedOverviewScenario(out var lunch);

        // Act
        var lunchOnly = store.ReadDesktopSnapshotForFilter(filter with { CategoryId = lunch });

        // Assert
        Assert.Equal(400, lunchOnly.MonthlyExpenses.Centimes);
        Assert.Equal(700, lunchOnly.MonthlyIncome.Centimes);
        Assert.Equal("Lunch", lunchOnly.LargestCategories.Single().Name);
        Assert.Equal(400, lunchOnly.LargestCategories.Single().Amount.Centimes);
    }

    [Fact]
    public void ReadFlowAverages_CategoryAndCustomRange_IncludesZeroCalendarPeriods()
    {
        // Arrange
        var account = store.SaveAccount(null, "Everyday", new DateOnly(2025, 8, 1), Money.Zero);
        var food = store.SaveCategory(null, "Food", null);
        var travel = store.SaveCategory(null, "Travel", null);
        Entry(account, "food in August", 10, food, new DateOnly(2025, 8, 3));
        Entry(account, "food in September", 20, food, new DateOnly(2025, 9, 3));
        Entry(account, "income in September", 50, food, new DateOnly(2025, 9, 4), TransactionKind.Income);
        Entry(account, "travel in October", 30, travel, new DateOnly(2025, 10, 3));

        // Act
        var allMonths = store.ReadFlowAverages(new TransactionsFilter(CategoryId: food,
            To: new DateOnly(2025, 10, 31)), AverageInterval.Month);
        var customMonths = store.ReadFlowAverages(new TransactionsFilter(CategoryId: food,
            From: new DateOnly(2025, 9, 1), To: new DateOnly(2025, 10, 31)), AverageInterval.Month);
        var octoberDays = store.ReadFlowAverages(new TransactionsFilter(CategoryId: food,
            From: new DateOnly(2025, 10, 1), To: new DateOnly(2025, 10, 3)), AverageInterval.Day);
        var weeks = store.ReadFlowAverages(new TransactionsFilter(CategoryId: food,
            From: new DateOnly(2025, 8, 3), To: new DateOnly(2025, 9, 3)), AverageInterval.Week);
        var years = store.ReadFlowAverages(new TransactionsFilter(CategoryId: food,
            From: new DateOnly(2025, 8, 1), To: new DateOnly(2026, 1, 1)), AverageInterval.Year);

        // Assert
        Assert.Equal(1000, allMonths.Expenses.Centimes);
        Assert.Equal(1667, allMonths.Income.Centimes);
        Assert.Equal(1000, customMonths.Expenses.Centimes);
        Assert.Equal(2500, customMonths.Income.Centimes);
        Assert.Equal(Money.Zero, octoberDays.Expenses);
        Assert.Equal(Money.Zero, octoberDays.Income);
        Assert.Equal(500, weeks.Expenses.Centimes);
        Assert.Equal(1500, years.Expenses.Centimes);
    }

    [Fact]
    public void PrepareSyntheticManualUiDataset_WhenRequested_WritesLedger()
    {
        var directory = Environment.GetEnvironmentVariable("BALANCIA_UI_TEST_DIR");
        Assert.SkipWhen(directory is null, "Set BALANCIA_UI_TEST_DIR to prepare the manual UI dataset.");

        // Arrange
        Directory.CreateDirectory(directory!);
        var uiStore = new LedgerStore(Path.Combine(directory!, "balancia.db"));
        uiStore.Initialize();
        var a = uiStore.SaveAccount(null, "Test wallet", Start, Money.FromFrancs(1000));
        var b = uiStore.SaveAccount(null, "Test savings", Start, new Money(0));
        var food = uiStore.SaveCategory(null, "Food", null);
        var lunch = uiStore.SaveCategory(null, "Lunch", food);

        // Act
        for (var i = 0; i < 220; i++)
        {
            uiStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, Start.AddDays(i % 10),
                i % 2 == 0 ? "Synthetic lunch" : "Synthetic groceries", Money.FromFrancs(1 + i % 5),
                a, CategoryId: i % 2 == 0 ? lunch : food));
        }

        uiStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, Start.AddDays(3), "Synthetic transfer",
            Money.FromFrancs(25), a, b));

        // Assert
        Assert.Equal(221, uiStore.ReadSnapshot().Entries.Count);
    }

    private TransactionsFilter SeedOverviewScenario(out string lunch)
    {
        var account = Account("Everyday");
        var other = Account("Other");
        var food = store.SaveCategory(null, "Food", null);
        lunch = store.SaveCategory(null, "Lunch", food);
        var dinner = store.SaveCategory(null, "Dinner", food);
        var car = store.SaveCategory(null, "Car", null);
        Entry(account, "match", 3, food);
        Entry(account, "match", 4, lunch);
        Entry(account, "match", 5, dinner);
        Entry(account, "match", 7, lunch, kind: TransactionKind.Income);
        Entry(account, "different", 9, lunch);
        Entry(account, "match", 8, car);
        Entry(other, "match", 6, lunch);
        return new TransactionsFilter(Description: "MATCH", AccountId: account,
            CategoryId: food, From: Start.AddDays(1), To: Start.AddDays(1),
            Minimum: Money.FromFrancs(3), Maximum: Money.FromFrancs(7));
    }

    /// <summary>The count query and the paged select spell category and amount matching in two SQL forms; this keeps them equal.</summary>
    [Fact]
    public void ReadTransactions_EveryFilterCombination_TotalCountEqualsUnpagedHits()
    {
        // Arrange
        var a = Account("A");
        var b = Account("B");
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        var other = store.SaveCategory(null, "Travel", null);
        Entry(a, "Lunch at work", 12.50m, child);
        Entry(a, "Lunch at work", 12.50m, parent, Start.AddDays(2));
        Entry(a, "Train", 40m, other, Start.AddDays(3));
        Entry(b, "Lunch at work", 12.50m, child, Start.AddDays(4));
        Entry(b, "Salary", 900m, kind: TransactionKind.Income);
        Entry(a, "Uncategorized", 7m, date: Start.AddDays(5));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, Start.AddDays(2), "Move",
            Money.FromFrancs(12.50m), a, b));
        var filters = new[]
        {
            new TransactionsFilter(),
            new TransactionsFilter(CategoryId: parent),
            new TransactionsFilter(CategoryId: child),
            new TransactionsFilter(CategoryIds: [parent, other]),
            new TransactionsFilter(Minimum: Money.FromFrancs(12.50m)),
            new TransactionsFilter(Maximum: Money.FromFrancs(12.50m)),
            new TransactionsFilter(Minimum: Money.FromFrancs(12.50m), Maximum: Money.FromFrancs(12.50m)),
            new TransactionsFilter(AccountId: b, Minimum: Money.FromFrancs(10m)),
            new TransactionsFilter(AccountIds: [a, b], Kind: TransactionKind.Transfer, Maximum: Money.FromFrancs(20m)),
            new TransactionsFilter("lunch", a, TransactionKind.Expense, parent, Start.AddDays(1), Start.AddDays(3),
                Money.FromFrancs(1m), Money.FromFrancs(50m))
        };

        foreach (var filter in filters)
        {
            // Act
            var page = store.ReadAllTransactions(filter);

            // Assert
            Assert.Equal(page.Hits.Count, page.TotalCount);
            Assert.Equal(page.Hits.Count, page.Hits.Select(hit => hit.Entry.Id).Distinct().Count());
            Assert.Equal(page.TotalCount, store.ReadTransactions(filter, pageSize: 1).TotalCount);
        }
    }

    public void Dispose() => File.Delete(path);
}
