using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class LedgerReaderDashboardTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-viewer-{Guid.NewGuid()}.db");

    [Fact]
    public void ReadDashboard_FilteredPeriod_PreservesBalancesAndSharesFlowAndReminderRules()
    {
        // Arrange
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
        var store = new LedgerStore(path, clock);
        store.Initialize();
        var account = store.SaveAccount(null, "Synthetic cash", new DateOnly(2026, 1, 1), new Money(10000));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 9, 1), "Earlier income", new Money(5000), account));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 10, 5), "Rent", new Money(1200), account));
        store.SaveReminder(null, "Rent", new DateOnly(2026, 10, 5), new Money(1200), 1);
        var reader = LedgerReader.Open(path, clock);
        var filter = new TransactionsFilter(From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 31));
        var identity = reader.ReadIdentity();

        // Act
        var dashboard = reader.ReadDashboard(filter);
        var flow = reader.ReadFlowAnalytics(filter);
        var averages = reader.ReadFlowAverages(filter);
        var reminder = reader.ReadReminders().Single();

        // Assert
        Assert.Equal(new Money(13800), dashboard.NetWorth);
        Assert.Equal(Money.Zero, dashboard.MonthlyIncome);
        Assert.Equal(new Money(1200), dashboard.MonthlyExpenses);
        Assert.Equal(new Money(1200), flow.Trend(FlowInterval.Month).Single().Expenses);
        Assert.Equal(new Money(1200), averages.Expenses);
        Assert.Equal(new DateOnly(2026, 11, 5), reminder.Occurrence);
        Assert.Equal(identity, reader.ReadIdentity());
        Assert.Empty(reader.ReadDashboard(filter with { AccountId = "absent" }).LargestCategories);
        Assert.Empty(reader.ReadFlowAnalytics(filter with { AccountId = "absent" }).Current);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
