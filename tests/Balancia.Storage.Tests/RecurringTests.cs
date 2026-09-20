using Balancia.Core;
using Balancia.Storage;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class RecurringTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "balancia-recurring-" + Guid.NewGuid() + ".db");
    private readonly LedgerStore _store;
    public RecurringTests()
    {
        _store = new(_path, new FixedClock());
        _store.Initialize();
        _store.SaveAccount(null, "Cash", new(2026, 1, 1), new(10000));
    }

    [Fact]
    public void ExactDescriptionAndMonthSatisfyOccurrence()
    {
        _store.SaveRecurringTemplate(null, "Rent", new(2026, 1, 28), new(1200), 1);
        var account = _store.ReadSnapshot().Accounts.Single().Id;
        _store.SaveTransaction(null, new(TransactionKind.Expense, new(2026, 1, 3), "Rent", new(1200), account));
        var reminder = _store.ReadRecurringReminders(new(2026, 1, 31)).Single();
        Assert.Equal(new DateOnly(2026, 2, 28), reminder.Occurrence);
        Assert.False(reminder.Overdue);
    }

    [Fact]
    public void EarlierPaymentDoesNotSatisfyCurrentMonth()
    {
        _store.SaveRecurringTemplate(null, "Tax", new(2026, 3, 15), new(100), 3);
        var account = _store.ReadSnapshot().Accounts.Single().Id;
        _store.SaveTransaction(null, new(TransactionKind.Expense, new(2026, 1, 28), "Tax", new(100), account));
        var reminder = _store.ReadRecurringReminders(new(2026, 3, 20)).Single();
        Assert.Equal(new DateOnly(2026, 3, 15), reminder.Occurrence);
        Assert.True(reminder.Overdue);
    }

    [Fact]
    public void MonthEndClampsWithoutPermanentDrift()
    {
        _store.SaveRecurringTemplate(null, "Fee", new(2026, 1, 31), new(10), 1);
        var account = _store.ReadSnapshot().Accounts.Single().Id;
        _store.SaveTransaction(null, new(TransactionKind.Expense, new(2026, 1, 31), "Fee", new(10), account));
        _store.SaveTransaction(null, new(TransactionKind.Expense, new(2026, 2, 28), "Fee", new(10), account));
        var reminders = _store.ReadRecurringReminders(new(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), reminders.Single().Occurrence);
    }

    [Fact]
    public void DuplicateActiveDescriptionsAreRejected()
    {
        _store.SaveRecurringTemplate(null, "Rent", new(2026, 1, 1), new(10), 1);
        Assert.Throws<ArgumentException>(() => _store.SaveRecurringTemplate(null, " rent ", new(2026, 2, 1), new(10), 1));
    }

    public void Dispose() => File.Delete(_path);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero); public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
