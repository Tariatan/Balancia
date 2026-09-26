using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class FlowQueriesTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-flows-{Guid.NewGuid():N}.db");
    private readonly LedgerStore store;
    private readonly string account;
    private static readonly DateOnly Opening = new(2024, 1, 1);

    public FlowQueriesTests()
    {
        store = new LedgerStore(path);
        store.Initialize();
        account = store.SaveAccount(null, "Synthetic", Opening, Money.FromFrancs(1000));
    }

    [Fact]
    public void ReadFlowAnalytics_CombinedFiltersAndPriorPeriod_UsesIdenticalRules()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        var otherAccount = store.SaveAccount(null, "Other", Opening, Money.Zero);
        Add(new DateOnly(2025, 1, 2), 12.50m, category: child);
        Add(new DateOnly(2024, 12, 3), 12.50m, category: parent);
        Add(new DateOnly(2025, 1, 2), 20m, category: child);
        Add(new DateOnly(2025, 1, 2), 12.50m, category: child, source: otherAccount);
        Add(new DateOnly(2025, 1, 2), 12.50m, category: child, description: "Other");
        Add(new DateOnly(2025, 1, 2), 12.50m, category: child, kind: TransactionKind.Income);
        var filter = new HistoryFilter("mAtCh", account, TransactionKind.Expense, parent,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), Money.FromFrancs(12.50m), Money.FromFrancs(12.50m));

        // Act
        var result = store.ReadFlowAnalytics(filter);

        // Assert
        Assert.Equal(new DateOnly(2024, 12, 1), result.PreviousFrom);
        Assert.Equal(new DateOnly(2024, 12, 31), result.PreviousTo);
        Assert.Equal(Money.FromFrancs(12.50m), Assert.Single(result.Current).Expenses);
        Assert.Equal(Money.FromFrancs(12.50m), Assert.Single(result.Previous).Expenses);
        Assert.Equal(store.ReadDesktopSnapshotForFilter(filter).MonthlyExpenses, result.Trend(FlowInterval.Month)[0].Expenses);
    }

    [Fact]
    public void ReadFlowAnalytics_MoreThanHistoryPage_ExcludesTransfersAndOpeningAndKeepsNegativeSavings()
    {
        // Arrange
        var date = new DateOnly(2025, 2, 1);
        for (var index = 0; index < 125; index++)
        {
            Add(date, 0.10m);
        }

        Add(date, 5m, kind: TransactionKind.Income);
        var destination = store.SaveAccount(null, "Cash", Opening, Money.Zero);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, date, "Move",
            Money.FromFrancs(100), account, destination));

        // Act
        var result = store.ReadFlowAnalytics(new HistoryFilter());
        var bucket = result.Trend(FlowInterval.Month)[0];

        // Assert
        Assert.Equal(date, result.From);
        Assert.Equal(Money.FromFrancs(12.50m), bucket.Expenses);
        Assert.Equal(Money.FromFrancs(5m), bucket.Income);
        Assert.Equal(Money.FromFrancs(-7.50m), bucket.Savings);
        Assert.Empty(store.ReadFlowAnalytics(new HistoryFilter(Kind: TransactionKind.Transfer)).Current);
    }

    [Fact]
    public void Trend_LeapMonthAndEmptyBuckets_PreservesEndpointsAndZeroGaps()
    {
        // Arrange
        Add(new DateOnly(2024, 2, 29), 10m);
        Add(new DateOnly(2024, 4, 1), 15m);
        var data = store.ReadFlowAnalytics(new HistoryFilter(From: new DateOnly(2024, 2, 28), To: new DateOnly(2024, 4, 2)));

        // Act
        var buckets = data.Trend(FlowInterval.Month);

        // Assert
        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateOnly(2024, 2, 29), buckets[0].To);
        Assert.Equal(Money.Zero, buckets[1].Expenses);
        Assert.Equal(new DateOnly(2024, 4, 2), buckets[2].To);
        Assert.Equal(35, data.To.DayNumber - data.From.DayNumber + 1);
        Assert.Equal(35, data.PreviousTo!.Value.DayNumber - data.PreviousFrom!.Value.DayNumber + 1);
        Assert.Equal(35, data.Trend(FlowInterval.Day).Count);
    }

    [Fact]
    public void ReadFlowAnalytics_EmptyRangeOrSingleDay_ReturnsSafeBoundaries()
    {
        // Arrange
        var date = new DateOnly(2025, 2, 1);
        var filter = new HistoryFilter(From: date, To: date);

        // Act
        var data = store.ReadFlowAnalytics(filter);

        // Assert
        Assert.Empty(data.Current);
        Assert.Empty(data.Previous);
        Assert.Equal(date.AddDays(-1), data.PreviousFrom);
        Assert.Equal(Money.Zero, Assert.Single(data.Trend(FlowInterval.Day)).Savings);
    }

    [Fact]
    public void CategorySelection_OverlappingParentsAndChildren_FiltersEveryReadWithoutDuplicates()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Food", null);
        var child = store.SaveCategory(null, "Lunch", parent);
        var salary = store.SaveCategory(null, "Salary", null);
        var other = store.SaveCategory(null, "Other", null);
        var date = new DateOnly(2025, 1, 10);
        Add(date, 10m, category: parent);
        Add(date, 20m, category: child);
        Add(date, 100m, category: salary, kind: TransactionKind.Income);
        Add(date, 40m, category: other);
        Add(date, 50m);
        Add(new DateOnly(2024, 12, 10), 5m, category: child);
        var filter = new HistoryFilter(Description: "match", AccountId: account,
            From: new DateOnly(2025, 1, 1), To: new DateOnly(2025, 1, 31),
            CategoryIds: [parent, child, salary]);

        // Act
        var history = store.ReadHistory(filter, pageSize: 1);
        var totals = store.ReadDesktopSnapshotForFilter(filter);
        var flows = store.ReadFlowAnalytics(filter);

        // Assert
        Assert.Equal(3, history.TotalCount);
        Assert.Single(history.Hits);
        Assert.Equal(Money.FromFrancs(30), totals.MonthlyExpenses);
        Assert.Equal(Money.FromFrancs(100), totals.MonthlyIncome);
        Assert.Equal(totals.MonthlyExpenses, Assert.Single(flows.Current).Expenses);
        Assert.Equal(totals.MonthlyIncome, Assert.Single(flows.Current).Income);
        Assert.Equal(Money.FromFrancs(5), Assert.Single(flows.Previous).Expenses);
        Assert.Equal(1, store.ReadHistory(filter with
        {
            CategoryIds = [child]
        }).TotalCount);
        Assert.Equal(5, store.ReadHistory(filter with
        {
            CategoryIds = []
        }).TotalCount);
        Assert.Equal(2, store.ReadHistory(filter with
        {
            CategoryIds = null,
            CategoryId = parent
        }).TotalCount);
    }

    private void Add(DateOnly date, decimal amount, string? category = null, string? source = null,
        string description = "Match", TransactionKind kind = TransactionKind.Expense) =>
        store.SaveTransaction(null, new TransactionDraft(kind, date, description,
            Money.FromFrancs(amount), source ?? account, CategoryId: category));

    public void Dispose() => File.Delete(path);
}
