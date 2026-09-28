using Xunit;

namespace Balancia.Core.Tests;

public class CalendarWeekTests
{
    [Theory]
    [InlineData("2026-01-05", "2026-01-05")] // Monday
    [InlineData("2026-01-06", "2026-01-05")] // Tuesday
    [InlineData("2026-01-08", "2026-01-05")] // Thursday
    [InlineData("2026-01-11", "2026-01-05")] // Sunday
    public void StartOfWeek_ReturnsPrecedingOrSameMonday(string date, string expectedMonday) =>
        Assert.Equal(DateOnly.Parse(expectedMonday), CalendarWeek.StartOfWeek(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-01-05", "2026-01-11")] // Monday
    [InlineData("2026-01-08", "2026-01-11")] // Thursday
    [InlineData("2026-01-11", "2026-01-11")] // Sunday
    public void EndOfWeek_ReturnsFollowingOrSameSunday(string date, string expectedSunday) =>
        Assert.Equal(DateOnly.Parse(expectedSunday), CalendarWeek.EndOfWeek(DateOnly.Parse(date)));
}
