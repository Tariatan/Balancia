using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ReminderPresentationTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static Reminder Reminder(DateOnly occurrence, decimal francs = 10m, bool overdue = false, bool satisfied = false) =>
        new(new ReminderTemplate(Guid.NewGuid().ToString("N"), "Rent", occurrence, Money.FromFrancs(francs), 1, false),
            occurrence, overdue, satisfied);

    [Theory]
    [InlineData("2026-10-13", "#B58B00")] // due today
    [InlineData("2026-10-18", "#B58B00")] // last day of the five-day window
    [InlineData("2026-10-19", "#263C48")] // later this month
    [InlineData("2026-11-02", "#3989A7")] // next month
    [InlineData("2026-12-01", "#2C8B6D")] // two months out
    [InlineData("2026-10-05", "#263C48")] // earlier this month, not flagged overdue
    public void ColorFor_PendingReminder_FollowsHowSoonItIsDue(string occurrence, string expectedColor)
    {
        // Arrange
        var reminder = Reminder(DateOnly.Parse(occurrence));

        // Act
        var color = ReminderPresentation.ColorFor(reminder, Today);

        // Assert
        Assert.Equal(expectedColor, color);
    }

    [Fact]
    public void ColorFor_OverdueReminder_IsRed()
    {
        // Arrange
        var reminder = Reminder(Today.AddDays(-3), overdue: true);

        // Act
        var color = ReminderPresentation.ColorFor(reminder, Today);

        // Assert
        Assert.Equal("#B95D4D", color);
    }

    [Fact]
    public void ColorFor_SatisfiedReminder_WinsOverOverdueAndDueSoon()
    {
        // Arrange
        var overdue = Reminder(Today.AddDays(-3), overdue: true, satisfied: true);
        var dueSoon = Reminder(Today.AddDays(2), satisfied: true);

        // Act
        var colors = new[] { ReminderPresentation.ColorFor(overdue, Today), ReminderPresentation.ColorFor(dueSoon, Today) };

        // Assert
        Assert.All(colors, color => Assert.Equal("#263C48", color));
    }

    [Theory]
    [InlineData("2026-10-13", false, false, true)]
    [InlineData("2026-10-18", false, false, true)]
    [InlineData("2026-10-19", false, false, false)]
    [InlineData("2026-10-05", true, false, true)]
    [InlineData("2026-10-05", true, true, false)]
    [InlineData("2026-10-14", false, true, false)]
    public void IsEmphasized_Reminder_IsBoldOnlyWhenPendingAndOverdueOrDueSoon(string occurrence, bool overdue, bool satisfied, bool expected)
    {
        // Arrange
        var reminder = Reminder(DateOnly.Parse(occurrence), overdue: overdue, satisfied: satisfied);

        // Act
        var emphasized = ReminderPresentation.IsEmphasized(reminder, Today);

        // Assert
        Assert.Equal(expected, emphasized);
    }

    [Fact]
    public void Summarize_WithPendingReminderThisMonth_TotalsThisMonthOnly()
    {
        // Arrange
        var reminders = new[]
        {
            Reminder(new DateOnly(2026, 10, 20), 100m),
            Reminder(new DateOnly(2026, 10, 25), 50m),
            Reminder(new DateOnly(2026, 10, 10), 7m, satisfied: true),
            Reminder(new DateOnly(2026, 11, 3), 900m),
        };

        // Act
        var totals = ReminderPresentation.Summarize(reminders, Today);

        // Assert
        Assert.True(totals.UpcomingIsThisMonth);
        Assert.Equal(Money.FromFrancs(150m), totals.Upcoming);
        Assert.Equal(Money.FromFrancs(1057m), totals.Net);
    }

    [Fact]
    public void Summarize_WithNothingPendingThisMonth_TotalsNextMonth()
    {
        // Arrange
        var reminders = new[]
        {
            Reminder(new DateOnly(2026, 10, 10), 7m, satisfied: true),
            Reminder(new DateOnly(2026, 11, 3), 900m),
            Reminder(new DateOnly(2026, 12, 3), 40m),
        };

        // Act
        var totals = ReminderPresentation.Summarize(reminders, Today);

        // Assert
        Assert.False(totals.UpcomingIsThisMonth);
        Assert.Equal(Money.FromFrancs(900m), totals.Upcoming);
    }

    [Fact]
    public void Summarize_AtYearEnd_RollsNextMonthIntoJanuary()
    {
        // Arrange
        var december = new DateOnly(2026, 12, 20);
        var reminders = new[] { Reminder(new DateOnly(2027, 1, 5), 30m), Reminder(new DateOnly(2027, 2, 5), 99m) };

        // Act
        var totals = ReminderPresentation.Summarize(reminders, december);

        // Assert
        Assert.False(totals.UpcomingIsThisMonth);
        Assert.Equal(Money.FromFrancs(30m), totals.Upcoming);
    }

    [Fact]
    public void Summarize_WithNoReminders_IsZero()
    {
        // Arrange
        Reminder[] reminders = [];

        // Act
        var totals = ReminderPresentation.Summarize(reminders, Today);

        // Assert
        Assert.Equal(Money.Zero, totals.Net);
        Assert.Equal(Money.Zero, totals.Upcoming);
    }
}
