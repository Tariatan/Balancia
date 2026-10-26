using Xunit;

namespace Balancia.Core.Tests;

public class ReminderScheduleTests
{
    [Theory]
    [InlineData("2026-01-15", 1, 15, "2026-02-15")] // desired day fits the target month
    [InlineData("2026-12-15", 1, 15, "2027-01-15")] // crosses the year boundary
    [InlineData("2026-01-31", 3, 31, "2026-04-30")] // multi-month interval skips intermediate months
    [InlineData("2026-01-31", 1, 31, "2026-02-28")] // non-leap February clamps to 28
    [InlineData("2028-01-31", 1, 31, "2028-02-29")] // leap February clamps to 29
    [InlineData("2026-02-28", 1, 31, "2026-03-31")] // desired day restored after a clamped month
    [InlineData("2028-02-29", 12, 29, "2029-02-28")] // annual Feb 29 clamps in a non-leap year
    [InlineData("2031-02-28", 12, 29, "2032-02-29")] // annual Feb 29 restored on the next leap year
    public void AddMonthsClamped_DesiredDay_ClampsToTargetMonthLength(string start, int months, int desiredDay, string expected)
    {
        // Arrange
        var startDate = DateOnly.Parse(start);

        // Act
        var result = ReminderSchedule.AddMonthsClamped(startDate, months, desiredDay);

        // Assert
        Assert.Equal(DateOnly.Parse(expected), result);
    }

    private static readonly DateOnly Today = new(2026, 10, 13);

    private static ReminderTemplate Template(string expected, int intervalMonths = 1) =>
        new("id", "Rent", DateOnly.Parse(expected), Money.FromFrancs(10m), intervalMonths, false);

    private static Func<DateOnly, bool> PaidIn(params string[] months) =>
        date => months.Contains($"{date.Year:0000}-{date.Month:00}");

    [Fact]
    public void Resolve_NoPaymentInTheExpectedMonth_StaysOnTheExpectedDate()
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template("2026-10-20"), Today, PaidIn());

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 20), reminder.Occurrence);
        Assert.False(reminder.Overdue);
    }

    [Fact]
    public void Resolve_PaymentInTheExpectedMonth_AdvancesToTheNextUnpaidMonth()
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template("2026-09-15"), Today, PaidIn("2026-09", "2026-10"));

        // Assert
        Assert.Equal(new DateOnly(2026, 11, 15), reminder.Occurrence);
    }

    [Fact]
    public void Resolve_PaymentOnlyInAnEarlierMonth_KeepsTheExpectedDateOverdue()
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template("2026-10-05"), Today, PaidIn("2026-09"));

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 5), reminder.Occurrence);
        Assert.True(reminder.Overdue);
    }

    [Fact]
    public void Resolve_PaymentOnlyInALaterMonth_DoesNotSatisfyTheExpectedOccurrence()
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template("2026-10-20"), Today, PaidIn("2026-11"));

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 20), reminder.Occurrence);
    }

    [Theory]
    [InlineData("2026-10-12", true)]
    [InlineData("2026-10-13", false)] // due today is not overdue
    [InlineData("2026-10-14", false)]
    public void Resolve_OccurrenceAgainstToday_IsOverdueOnlyBeforeToday(string expected, bool overdue)
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template(expected), Today, PaidIn());

        // Assert
        Assert.Equal(overdue, reminder.Overdue);
    }

    [Fact]
    public void Resolve_MonthEndAnchorAfterShortMonth_ClampsWithoutPermanentDrift()
    {
        // Act
        var afterJanuary = ReminderSchedule.Resolve(Template("2026-01-31"), Today, PaidIn("2026-01"));
        var afterFebruary = ReminderSchedule.Resolve(Template("2026-01-31"), Today, PaidIn("2026-01", "2026-02"));

        // Assert
        Assert.Equal(new DateOnly(2026, 2, 28), afterJanuary.Occurrence);
        Assert.Equal(new DateOnly(2026, 3, 31), afterFebruary.Occurrence);
    }

    [Fact]
    public void Resolve_MultiMonthInterval_SkipsIntermediateMonths()
    {
        // Act
        var reminder = ReminderSchedule.Resolve(Template("2026-01-10", intervalMonths: 3), Today, PaidIn("2026-01", "2026-04", "2026-07"));

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 10), reminder.Occurrence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_NonPositiveInterval_Throws(int intervalMonths)
    {
        // Arrange
        var template = Template("2026-10-20", intervalMonths);

        // Act
        Action act = () => ReminderSchedule.Resolve(template, Today, PaidIn("2026-10"));

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-31", 0)]
    [InlineData("2026-11-30", "2026-10-01", 1)]
    [InlineData("2027-01-01", "2026-12-31", 1)] // year boundary
    [InlineData("2026-12-01", "2026-10-13", 2)]
    [InlineData("2026-09-30", "2026-10-01", -1)]
    public void MonthsUntil_Dates_CountsCalendarMonthsNotDays(string occurrence, string today, int expected)
    {
        // Act
        var months = ReminderSchedule.MonthsUntil(DateOnly.Parse(occurrence), DateOnly.Parse(today));

        // Assert
        Assert.Equal(expected, months);
    }
}
