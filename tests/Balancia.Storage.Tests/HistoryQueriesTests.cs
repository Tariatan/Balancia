using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class HistoryQueriesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "balancia-history-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Clock _clock = new();
    private readonly LedgerStore _store;
    private static readonly DateOnly Start = new(2026, 9, 1);

    public HistoryQueriesTests()
    {
        _store = new(_path, _clock);
        _store.Initialize();
    }
    private string Account(string name) => _store.SaveAccount(null, name, Start, new Money(0));
    private string Entry(string account, string text, decimal francs, string? category = null, DateOnly? date = null,
        TransactionKind kind = TransactionKind.Expense) => _store.SaveTransaction(null,
        new(kind, date ?? Start.AddDays(1), text, Money.FromFrancs(francs), account, CategoryId: category));

    [Fact]
    public void H01_CombinedSearchDateAmountAccountAndTypeUseInclusiveEndpoints()
    {
        var a = Account("A");
        var b = Account("B");
        var match = Entry(a, "Lunch At WORK", 12.50m);
        Entry(a, "Lunch at home", 12.50m, date: Start.AddDays(2));
        Entry(b, "Lunch at work", 12.50m);
        Entry(a, "Lunch at work", 13m);
        Entry(a, "Lunch at work", 12.50m, kind: TransactionKind.Income);
        Entry(a, "Crème brûlée", 2m);
        var page = _store.ReadHistory(new("lUnCh aT wOrK", a, TransactionKind.Expense, null,
            Start.AddDays(1), Start.AddDays(1), Money.FromFrancs(12.50m), Money.FromFrancs(12.50m)));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match, page.Hits.Single().Entry.Id);
        Assert.Equal(-1250, page.Hits.Single().AccountEffect?.Centimes);
        Assert.Equal(1, _store.ReadHistory(new(Description: "CRÈME")).TotalCount);
    }

    [Fact]
    public void H02_ParentCategoryIncludesOwnAndChildEntries()
    {
        var a = Account("A");
        var food = _store.SaveCategory(null, "Food", null);
        var lunch = _store.SaveCategory(null, "Lunch", food);
        var car = _store.SaveCategory(null, "Car", null);
        Entry(a, "one", 1, food);
        Entry(a, "two", 2, lunch);
        Entry(a, "three", 3, car);
        Assert.Equal(2, _store.ReadHistory(new(CategoryId: food)).TotalCount);
        Assert.Equal(1, _store.ReadHistory(new(CategoryId: lunch)).TotalCount);
    }

    [Fact]
    public void H03_PagesHaveStableOrderingAndTransferEffects()
    {
        var a = Account("A");
        var b = Account("B");
        for (var i = 0; i < 29; i++)
        {
            Entry(a, "same day " + i, 1);
        }

        _store.SaveTransaction(null, new(TransactionKind.Transfer, Start.AddDays(1), "Move", Money.FromFrancs(5), a, b));
        var first = _store.ReadHistory(new(), 0, 7);
        var all = new List<string>();
        var page = first;
        while (page.Hits.Count > 0)
        {
            all.AddRange(page.Hits.Select(h => h.Entry.Id));
            var last = page.Hits[^1].Entry;
            page = _store.ReadHistoryAfter(new(), last.Draft.Date, last.Id, 7);
        }
        Assert.Equal(30, all.Count);
        Assert.Equal(30, all.Distinct().Count());
        Assert.Equal(_store.ReadSnapshot().Entries.Select(e => e.Id), all);
        Assert.Equal(9, _store.FindHistoryOffset(all[9]));
        var transfer = _store.ReadHistory(new(AccountId: b)).Hits.Single();
        Assert.Equal(500, transfer.AccountEffect?.Centimes);
    }

    [Fact]
    public void OverviewReadIncludesEveryTransactionInSelectedPeriod()
    {
        var account = Account("Everyday");
        Entry(account, "outside", 1, date: Start.AddDays(1));
        for (var i = 0; i < 1005; i++)
        {
            Entry(account, "inside " + i, 1, date: Start.AddDays(2));
        }

        var page = _store.ReadAllHistory(new(From: Start.AddDays(2), To: Start.AddDays(2)));

        Assert.Equal(1005, page.TotalCount);
        Assert.Equal(1005, page.Hits.Count);
        Assert.Equal(1005, page.Hits.Select(hit => hit.Entry.Id).Distinct().Count());
        Assert.DoesNotContain(page.Hits, hit => hit.Entry.Draft.Description == "outside");
    }

    [Fact]
    public void H04_H05_DesktopTotalsRefreshAfterEditsAndMonthChange()
    {
        var a = Account("A");
        var id = Entry(a, "Expense", 10);
        Assert.Equal(1000, _store.ReadDesktopSnapshot().MonthlyExpenses.Centimes);
        var original = _store.ReadHistory(new()).Hits.Single().Entry.Draft;
        _store.SaveTransaction(id, original with
        {
            Amount = Money.FromFrancs(15)
        });
        Assert.Equal(1500, _store.ReadDesktopSnapshot().MonthlyExpenses.Centimes);
        Assert.Equal(1500, _store.ReadHistory(new()).Hits.Single().Entry.Draft.Amount.Centimes);
        _clock.Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, _store.ReadDesktopSnapshot().MonthlyExpenses.Centimes);
        _store.DeleteTransaction(id);
        Assert.Empty(_store.ReadHistory(new()).Hits);
        Assert.Equal(0, _store.ReadDesktopSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void OverviewPeriodChangesFlowsCategoriesAndHistoryButNotBalances()
    {
        var account = _store.SaveAccount(null, "Everyday", new DateOnly(2026, 8, 1), new Money(0));
        var food = _store.SaveCategory(null, "Food", null);
        var transport = _store.SaveCategory(null, "Transport", null);
        Entry(account, "August groceries", 10, food, new DateOnly(2026, 8, 31));
        Entry(account, "September train", 20, transport, Start);
        Entry(account, "September salary", 50, date: Start.AddDays(17), kind: TransactionKind.Income);

        var august = _store.ReadDesktopSnapshotForPeriod(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var september = _store.ReadDesktopSnapshotForPeriod(Start, Start.AddDays(17));
        var all = _store.ReadDesktopSnapshotForPeriod(null, null);

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
        Assert.Equal(1, _store.ReadHistory(new(From: new DateOnly(2026, 8, 31), To: new DateOnly(2026, 8, 31))).TotalCount);
        Assert.Throws<ArgumentException>(() => _store.ReadDesktopSnapshotForPeriod(Start, new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void PrepareSyntheticManualUiDatasetWhenRequested()
    {
        var directory = Environment.GetEnvironmentVariable("BALANCIA_UI_TEST_DIR");
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var store = new LedgerStore(Path.Combine(directory, "balancia.db"));
        store.Initialize();
        var a = store.SaveAccount(null, "Test wallet", Start, Money.FromFrancs(1000));
        var b = store.SaveAccount(null, "Test savings", Start, new Money(0));
        var food = store.SaveCategory(null, "Food", null);
        var lunch = store.SaveCategory(null, "Lunch", food);
        for (var i = 0; i < 220; i++)
        {
            store.SaveTransaction(null, new(TransactionKind.Expense, Start.AddDays(i % 10),
                i % 2 == 0 ? "Synthetic lunch" : "Synthetic groceries", Money.FromFrancs(1 + i % 5),
                a, CategoryId: i % 2 == 0 ? lunch : food));
        }

        store.SaveTransaction(null, new(TransactionKind.Transfer, Start.AddDays(3), "Synthetic transfer",
            Money.FromFrancs(25), a, b));
    }

    public void Dispose() => File.Delete(_path);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
