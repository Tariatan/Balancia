using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class RemindersTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "balancia-reminders-" + Guid.NewGuid() + ".db");
    private readonly LedgerStore store;
    private readonly string account;

    public RemindersTests()
    {
        store = new LedgerStore(path, new TestClock(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        store.Initialize();
        account = store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(10000));
    }

    [Fact]
    public void ReadReminders_PaymentWithExactDescriptionInMonth_AdvancesToNextOccurrence()
    {
        // Arrange
        store.SaveReminder(null, "Rent", new DateOnly(2026, 1, 28), new Money(1200), 1);
        Pay("Rent", 1200, new DateOnly(2026, 1, 3));

        // Act
        var reminder = store.ReadReminders(new DateOnly(2026, 1, 31)).Single();

        // Assert
        Assert.Equal(new DateOnly(2026, 2, 28), reminder.Occurrence);
        Assert.False(reminder.Overdue);
    }

    [Fact]
    public void ReadReminders_PaymentInEarlierMonth_KeepsCurrentOccurrenceOverdue()
    {
        // Arrange
        store.SaveReminder(null, "Tax", new DateOnly(2026, 3, 15), new Money(100), 3);
        Pay("Tax", 100, new DateOnly(2026, 1, 28));

        // Act
        var reminder = store.ReadReminders(new DateOnly(2026, 3, 20)).Single();

        // Assert
        Assert.Equal(new DateOnly(2026, 3, 15), reminder.Occurrence);
        Assert.True(reminder.Overdue);
    }

    [Fact]
    public void ReadReminders_MonthEndAnchorAfterShortMonth_ClampsWithoutPermanentDrift()
    {
        // Arrange
        store.SaveReminder(null, "Fee", new DateOnly(2026, 1, 31), new Money(10), 1);
        Pay("Fee", 10, new DateOnly(2026, 1, 31));
        Pay("Fee", 10, new DateOnly(2026, 2, 28));

        // Act
        var reminders = store.ReadReminders(new DateOnly(2026, 3, 1));

        // Assert
        Assert.Equal(new DateOnly(2026, 3, 31), reminders.Single().Occurrence);
    }

    [Fact]
    public void SaveReminder_DuplicateActiveDescription_ThrowsArgumentException()
    {
        // Arrange
        store.SaveReminder(null, "Rent", new DateOnly(2026, 1, 1), new Money(10), 1);

        // Act
        void Act() => store.SaveReminder(null, " rent ", new DateOnly(2026, 2, 1), new Money(10), 1);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    private void Pay(string description, long centimes, DateOnly date) =>
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, date, description, new Money(centimes), account));

    public void Dispose() => File.Delete(path);
}
