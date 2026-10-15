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
}
