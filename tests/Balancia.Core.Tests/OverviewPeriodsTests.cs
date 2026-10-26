using Xunit;

namespace Balancia.Core.Tests;

public class OverviewPeriodsTests
{
    // 2026-03-04 is a Wednesday; its week is Monday 2026-03-02 to Sunday 2026-03-08.
    private static readonly DateOnly Today = new(2026, 3, 4);

    [Theory]
    [InlineData(OverviewPeriod.Day, "2026-03-04", "2026-03-04")]
    [InlineData(OverviewPeriod.ThisWeek, "2026-03-02", "2026-03-08")]
    [InlineData(OverviewPeriod.ThisMonth, "2026-03-01", "2026-03-31")]
    [InlineData(OverviewPeriod.ThisYear, "2026-01-01", "2026-12-31")]
    [InlineData(OverviewPeriod.Last30Days, "2026-02-03", "2026-03-04")]
    public void Range_FixedPeriod_ResolvesAroundToday(OverviewPeriod period, string from, string to)
    {
        // Act
        var range = OverviewPeriods.Range(period, Today, DateOnly.Parse("2000-01-01"), DateOnly.Parse("2000-01-02"));

        // Assert
        Assert.Equal(new DateRange(DateOnly.Parse(from), DateOnly.Parse(to)), range);
    }

    [Fact]
    public void Range_All_IsOpenOnBothEnds()
    {
        // Act
        var range = OverviewPeriods.Range(OverviewPeriod.All, Today, new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 2));

        // Assert
        Assert.Equal(new DateRange(null, null), range);
    }

    [Theory]
    [InlineData("2026-01-10", "2026-02-20")]
    [InlineData("2026-01-10", null)]
    [InlineData(null, "2026-02-20")]
    [InlineData(null, null)]
    public void Range_Custom_ReturnsTheChosenBoundsIncludingOpenOnes(string? from, string? to)
    {
        // Arrange
        DateOnly? customFrom = from is null ? null : DateOnly.Parse(from);
        DateOnly? customTo = to is null ? null : DateOnly.Parse(to);

        // Act
        var range = OverviewPeriods.Range(OverviewPeriod.Custom, Today, customFrom, customTo);

        // Assert
        Assert.Equal(new DateRange(customFrom, customTo), range);
    }

    [Theory]
    [InlineData("2026-02-15", "2026-02-01", "2026-02-28")]
    [InlineData("2028-02-15", "2028-02-01", "2028-02-29")]
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")]
    public void Range_ThisMonth_EndsOnTheLastDayIncludingLeapFebruary(string today, string from, string to)
    {
        // Act
        var range = OverviewPeriods.Range(OverviewPeriod.ThisMonth, DateOnly.Parse(today), null, null);

        // Assert
        Assert.Equal(new DateRange(DateOnly.Parse(from), DateOnly.Parse(to)), range);
    }

    [Theory]
    [InlineData("2026-03-01", "2026-02-23", "2026-03-01")] // Sunday belongs to the week that started the Monday before
    [InlineData("2026-03-02", "2026-03-02", "2026-03-08")] // Monday starts a new week
    public void Range_ThisWeek_RunsMondayToSunday(string today, string from, string to)
    {
        // Act
        var range = OverviewPeriods.Range(OverviewPeriod.ThisWeek, DateOnly.Parse(today), null, null);

        // Assert
        Assert.Equal(new DateRange(DateOnly.Parse(from), DateOnly.Parse(to)), range);
    }

    [Theory]
    [InlineData(OverviewPeriod.Day, AverageInterval.Day)]
    [InlineData(OverviewPeriod.ThisWeek, AverageInterval.Week)]
    [InlineData(OverviewPeriod.ThisYear, AverageInterval.Year)]
    [InlineData(OverviewPeriod.ThisMonth, AverageInterval.Month)]
    [InlineData(OverviewPeriod.All, AverageInterval.Month)]
    [InlineData(OverviewPeriod.Last30Days, AverageInterval.Month)]
    [InlineData(OverviewPeriod.Custom, AverageInterval.Month)]
    public void IntervalFor_Period_ReturnsTheUnitItAveragesPer(OverviewPeriod period, AverageInterval expected)
    {
        // Act
        var interval = OverviewPeriods.IntervalFor(period);

        // Assert
        Assert.Equal(expected, interval);
    }

    [Theory]
    [InlineData(OverviewPeriod.Custom, true)]
    [InlineData(OverviewPeriod.All, false)]
    [InlineData(OverviewPeriod.Day, false)]
    [InlineData(OverviewPeriod.ThisWeek, false)]
    [InlineData(OverviewPeriod.ThisMonth, false)]
    [InlineData(OverviewPeriod.ThisYear, false)]
    [InlineData(OverviewPeriod.Last30Days, false)]
    public void AverageFrom_Period_KeepsTheStartOnlyForACustomRange(OverviewPeriod period, bool keepsStart)
    {
        // Arrange
        DateOnly? effectiveFrom = new DateOnly(2026, 3, 1);

        // Act
        var from = OverviewPeriods.AverageFrom(period, effectiveFrom);

        // Assert
        Assert.Equal(keepsStart ? effectiveFrom : null, from);
    }

    [Fact]
    public void AverageFrom_CustomWithOpenStart_StaysOpen()
    {
        // Act
        var from = OverviewPeriods.AverageFrom(OverviewPeriod.Custom, null);

        // Assert
        Assert.Null(from);
    }

    [Theory]
    [InlineData(AverageInterval.Day, "2026-03-01", "2026-03-01", 1)]
    [InlineData(AverageInterval.Day, "2026-02-27", "2026-03-02", 4)]
    [InlineData(AverageInterval.Week, "2026-03-01", "2026-03-02", 2)] // Sunday and the following Monday touch two weeks
    [InlineData(AverageInterval.Week, "2026-03-02", "2026-03-08", 1)]
    [InlineData(AverageInterval.Week, "2025-08-03", "2025-09-03", 6)]
    [InlineData(AverageInterval.Month, "2026-01-31", "2026-02-01", 2)]
    [InlineData(AverageInterval.Month, "2025-09-01", "2025-10-31", 2)]
    [InlineData(AverageInterval.Month, "2025-11-15", "2026-02-15", 4)]
    [InlineData(AverageInterval.Year, "2025-12-31", "2026-01-01", 2)]
    [InlineData(AverageInterval.Year, "2025-08-01", "2026-01-01", 2)]
    public void CountPeriods_InclusiveRange_CountsEveryTouchedCalendarUnit(AverageInterval interval, string from, string to, long expected)
    {
        // Act
        var count = interval.CountPeriods(DateOnly.Parse(from), DateOnly.Parse(to));

        // Assert
        Assert.Equal(expected, count);
    }

    [Fact]
    public void CountPeriods_UndefinedInterval_Throws()
    {
        // Arrange
        var interval = (AverageInterval)99;

        // Act
        Action act = () => _ = interval.CountPeriods(Today, Today);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }
}
