using Balancia.Core;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class OverviewLoaderTests : IDisposable
{
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-overview-{Guid.NewGuid():N}.db");
    private readonly LedgerStore store;
    private readonly string account;

    public OverviewLoaderTests()
    {
        store = new LedgerStore(path);
        store.Initialize();
        account = store.SaveAccount(null, "Everyday", Opening, Money.FromFrancs(1000));
        Spend(new DateOnly(2026, 8, 5), 30m, "August");
        Spend(new DateOnly(2026, 9, 5), 60m, "September");
        Spend(new DateOnly(2026, 10, 5), 90m, "October");
        store.SaveReminder(null, "Rent", new DateOnly(2026, 11, 1), Money.FromFrancs(1500m), 1);
    }

    private void Spend(DateOnly date, decimal francs, string description) =>
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, date, description, Money.FromFrancs(francs), account));

    private static TransactionsFilter October => new(From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 31));

    [Fact]
    public async Task LoadAsync_ThisMonth_ReturnsTheSameDataAsTheIndividualReads()
    {
        // Arrange
        var query = new OverviewQuery(October, 0, OverviewPeriod.ThisMonth);

        // Act
        var data = await OverviewLoader.LoadAsync(store, query);

        // Assert
        Assert.Same(query, data.Query);
        Assert.Equal(store.ReadDesktopSnapshotForFilter(October).MonthlyExpenses, data.Snapshot.MonthlyExpenses);
        Assert.Equal(Money.FromFrancs(90m), data.Snapshot.MonthlyExpenses);
        Assert.Equal(store.ReadTransactions(October).TotalCount, data.Transactions.TotalCount);
        Assert.Equal("Rent", Assert.Single(data.Reminders).Template.Description);
        Assert.Equal(store.ReadFlowAnalytics(October).Current.Count, data.Analytics.Current.Count);
    }

    [Fact]
    public async Task LoadAsync_PeriodWithoutItsOwnWindow_AveragesOverTheWholeLedgerUpToThePeriodEnd()
    {
        // Arrange
        var query = new OverviewQuery(October, 0, OverviewPeriod.ThisMonth);

        // Act
        var data = await OverviewLoader.LoadAsync(store, query);

        // Assert
        // August to October is three calendar months: (30 + 60 + 90) / 3.
        Assert.Equal(Money.FromFrancs(60m), data.Averages.Expenses);
    }

    [Fact]
    public async Task LoadAsync_CustomPeriod_AveragesOverItsOwnSpan()
    {
        // Arrange
        var query = new OverviewQuery(October, 0, OverviewPeriod.Custom);

        // Act
        var data = await OverviewLoader.LoadAsync(store, query);

        // Assert
        Assert.Equal(Money.FromFrancs(90m), data.Averages.Expenses);
    }

    [Fact]
    public async Task LoadAsync_PeriodWithAnAveragingUnit_AveragesPerThatUnit()
    {
        // Arrange
        var week = new TransactionsFilter(From: new DateOnly(2026, 10, 5), To: new DateOnly(2026, 10, 11));

        // Act
        var perWeek = await OverviewLoader.LoadAsync(store, new OverviewQuery(week, 0, OverviewPeriod.ThisWeek));
        var perMonth = await OverviewLoader.LoadAsync(store, new OverviewQuery(week, 0, OverviewPeriod.ThisMonth));

        // Assert
        // Weeks run Monday to Sunday: 2026-08-03 .. 2026-10-05 is 10 weeks, so 180 / 10 = 18 against 180 / 3 = 60 per month.
        Assert.Equal(Money.FromFrancs(18m), perWeek.Averages.Expenses);
        Assert.Equal(Money.FromFrancs(60m), perMonth.Averages.Expenses);
    }

    [Fact]
    public async Task LoadAsync_Offset_SkipsThatManyTransactions()
    {
        // Arrange
        var all = new TransactionsFilter();

        // Act
        var first = await OverviewLoader.LoadAsync(store, new OverviewQuery(all, 0, OverviewPeriod.All));
        var skipped = await OverviewLoader.LoadAsync(store, new OverviewQuery(all, 2, OverviewPeriod.All));

        // Assert
        Assert.Equal(3, first.Transactions.TotalCount);
        Assert.Equal(first.Transactions.Hits.Skip(2).Select(hit => hit.Entry.Id), skipped.Transactions.Hits.Select(hit => hit.Entry.Id));
        Assert.Equal(2, skipped.Transactions.Offset);
    }

    [Fact]
    public async Task LoadAsync_FilterMatchingNothing_ReturnsEmptyButCompleteData()
    {
        // Arrange
        var query = new OverviewQuery(new TransactionsFilter("no such description"), 0, OverviewPeriod.All);

        // Act
        var data = await OverviewLoader.LoadAsync(store, query);

        // Assert
        Assert.Equal(0, data.Transactions.TotalCount);
        Assert.Empty(data.Transactions.Hits);
        Assert.Equal(Money.Zero, data.Snapshot.MonthlyExpenses);
        Assert.Single(data.Reminders);
    }

    public void Dispose() => File.Delete(path);
}
