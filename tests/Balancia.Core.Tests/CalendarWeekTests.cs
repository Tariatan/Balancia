using Xunit;

namespace Balancia.Core.Tests;

public class CalendarWeekTests
{
    [Theory]
    [InlineData("2026-01-05", "2026-01-05")] // Monday
    [InlineData("2026-01-06", "2026-01-05")] // Tuesday
    [InlineData("2026-01-08", "2026-01-05")] // Thursday
    [InlineData("2026-01-11", "2026-01-05")] // Sunday
    public void StartOfWeek_AnyDayOfWeek_ReturnsPrecedingOrSameMonday(string date, string expectedMonday)
    {
        // Arrange
        var day = DateOnly.Parse(date);

        // Act
        var result = CalendarWeek.StartOfWeek(day);

        // Assert
        Assert.Equal(DateOnly.Parse(expectedMonday), result);
    }

    [Theory]
    [InlineData("2026-01-05", "2026-01-11")] // Monday
    [InlineData("2026-01-08", "2026-01-11")] // Thursday
    [InlineData("2026-01-11", "2026-01-11")] // Sunday
    public void EndOfWeek_AnyDayOfWeek_ReturnsFollowingOrSameSunday(string date, string expectedSunday)
    {
        // Arrange
        var day = DateOnly.Parse(date);

        // Act
        var result = CalendarWeek.EndOfWeek(day);

        // Assert
        Assert.Equal(DateOnly.Parse(expectedSunday), result);
    }
}
