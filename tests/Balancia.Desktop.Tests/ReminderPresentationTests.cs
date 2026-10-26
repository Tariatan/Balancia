using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ReminderPresentationTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static Reminder Reminder(DateOnly occurrence, decimal francs = 10m, bool overdue = false) =>
        new(new ReminderTemplate(Guid.NewGuid().ToString("N"), "Rent", occurrence, Money.FromFrancs(francs), 1, false),
            occurrence, overdue);

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

    [Theory]
    [InlineData("2026-10-31", false)] // this month
    [InlineData("2026-11-30", false)] // last day of next month
    [InlineData("2026-12-01", true)] // first day two months out
    [InlineData("2027-03-15", true)]
    [InlineData("2026-09-30", false)] // earlier months are never tinted
    public void IsBeyondNextMonth_Reminder_TintsOnlyFromTwoMonthsOut(string occurrence, bool expected)
    {
        // Arrange
        var reminder = Reminder(DateOnly.Parse(occurrence));

        // Act
        var beyond = ReminderPresentation.IsBeyondNextMonth(reminder, Today);

        // Assert
        Assert.Equal(expected, beyond);
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

    [Theory]
    [InlineData("2026-10-13", false, true)]
    [InlineData("2026-10-18", false, true)]
    [InlineData("2026-10-19", false, false)]
    [InlineData("2026-10-05", true, true)]
    public void IsEmphasized_Reminder_IsBoldOnlyWhenOverdueOrDueSoon(string occurrence, bool overdue, bool expected)
    {
        // Arrange
        var reminder = Reminder(DateOnly.Parse(occurrence), overdue: overdue);

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
            Reminder(new DateOnly(2026, 11, 3), 900m),
        };

        // Act
        var totals = ReminderPresentation.Summarize(reminders, Today);

        // Assert
        Assert.True(totals.UpcomingIsThisMonth);
        Assert.Equal(Money.FromFrancs(150m), totals.Upcoming);
        Assert.Equal(Money.FromFrancs(1050m), totals.Net);
    }

    [Fact]
    public void Summarize_WithNothingPendingThisMonth_TotalsNextMonth()
    {
        // Arrange
        var reminders = new[]
        {
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
