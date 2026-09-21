using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class RecurringTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "balancia-recurring-" + Guid.NewGuid() + ".db");
    private readonly LedgerStore store;
    public RecurringTests()
    {
        store = new LedgerStore(path, new FixedClock());
        store.Initialize();
        store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(10000));
    }

    [Fact]
    public void ExactDescriptionAndMonthSatisfyOccurrence()
    {
        store.SaveRecurringTemplate(null, "Rent", new DateOnly(2026, 1, 28), new Money(1200), 1);
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 3), "Rent", new Money(1200), account));
        var reminder = store.ReadRecurringReminders(new DateOnly(2026, 1, 31)).Single();
        Assert.Equal(new DateOnly(2026, 2, 28), reminder.Occurrence);
        Assert.False(reminder.Overdue);
    }

    [Fact]
    public void EarlierPaymentDoesNotSatisfyCurrentMonth()
    {
        store.SaveRecurringTemplate(null, "Tax", new DateOnly(2026, 3, 15), new Money(100), 3);
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 28), "Tax", new Money(100), account));
        var reminder = store.ReadRecurringReminders(new DateOnly(2026, 3, 20)).Single();
        Assert.Equal(new DateOnly(2026, 3, 15), reminder.Occurrence);
        Assert.True(reminder.Overdue);
    }

    [Fact]
    public void MonthEndClampsWithoutPermanentDrift()
    {
        store.SaveRecurringTemplate(null, "Fee", new DateOnly(2026, 1, 31), new Money(10), 1);
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 31), "Fee", new Money(10), account));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 2, 28), "Fee", new Money(10), account));
        var reminders = store.ReadRecurringReminders(new DateOnly(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), reminders.Single().Occurrence);
    }

    [Fact]
    public void DuplicateActiveDescriptionsAreRejected()
    {
        store.SaveRecurringTemplate(null, "Rent", new DateOnly(2026, 1, 1), new Money(10), 1);
        Assert.Throws<ArgumentException>(() => store.SaveRecurringTemplate(null, " rent ", new DateOnly(2026, 2, 1), new Money(10), 1));
    }

    public void Dispose() => File.Delete(path);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero); public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
