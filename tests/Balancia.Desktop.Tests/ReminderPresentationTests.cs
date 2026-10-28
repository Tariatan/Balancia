using Avalonia.Media;
using Balancia.Core;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ReminderPresentationTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static Reminder Reminder(DateOnly occurrence, decimal francs = 10m, bool overdue = false) =>
        new(new ReminderTemplate(Guid.NewGuid().ToString("N"), "Rent", occurrence, Money.FromFrancs(francs), 1, false),
            occurrence, overdue);

    private static IBrush Role(string name) => name switch
    {
        nameof(Palette.Warning) => Palette.Warning,
        nameof(Palette.Text) => Palette.Text,
        nameof(Palette.Accent) => Palette.Accent,
        nameof(Palette.Positive) => Palette.Positive,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [AvaloniaTheory]
    [InlineData("2026-10-13", nameof(Palette.Warning))] // due today
    [InlineData("2026-10-18", nameof(Palette.Warning))] // last day of the five-day window
    [InlineData("2026-10-19", nameof(Palette.Text))] // later this month
    [InlineData("2026-11-02", nameof(Palette.Accent))] // next month
    [InlineData("2026-12-01", nameof(Palette.Positive))] // two months out
    [InlineData("2026-10-05", nameof(Palette.Text))] // earlier this month, not flagged overdue
    public void BrushFor_PendingReminder_FollowsHowSoonItIsDue(string occurrence, string expectedRole)
    {
        // Arrange
        var reminder = Reminder(DateOnly.Parse(occurrence));

        // Act
        var brush = ReminderPresentation.BrushFor(reminder, Today);

        // Assert
        Assert.Same(Role(expectedRole), brush);
    }

    [AvaloniaTheory]
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

    [AvaloniaFact]
    public void BrushFor_OverdueReminder_IsTheNegativeBrush()
    {
        // Arrange
        var reminder = Reminder(Today.AddDays(-3), overdue: true);

        // Act
        var brush = ReminderPresentation.BrushFor(reminder, Today);

        // Assert
        Assert.Same(Palette.Negative, brush);
    }

    [AvaloniaTheory]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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
