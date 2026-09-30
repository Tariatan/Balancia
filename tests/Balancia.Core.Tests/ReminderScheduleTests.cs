using Xunit;

namespace Balancia.Core.Tests;

public class ReminderScheduleTests
{
    [Fact]
    public void AddMonthsClamped_DesiredDayFitsTargetMonth_KeepsDesiredDay() =>
        Assert.Equal(new DateOnly(2026, 2, 15), ReminderSchedule.AddMonthsClamped(new DateOnly(2026, 1, 15), 1, 15));

    [Fact]
    public void AddMonthsClamped_CrossesYearBoundary_AdvancesYear() =>
        Assert.Equal(new DateOnly(2027, 1, 15), ReminderSchedule.AddMonthsClamped(new DateOnly(2026, 12, 15), 1, 15));

    [Fact]
    public void AddMonthsClamped_MultiMonthInterval_SkipsIntermediateMonths() =>
        Assert.Equal(new DateOnly(2026, 4, 30), ReminderSchedule.AddMonthsClamped(new DateOnly(2026, 1, 31), 3, 31));

    [Fact]
    public void AddMonthsClamped_JanuaryThirtyFirstIntoNonLeapFebruary_ClampsToTwentyEight() =>
        Assert.Equal(new DateOnly(2026, 2, 28), ReminderSchedule.AddMonthsClamped(new DateOnly(2026, 1, 31), 1, 31));

    [Fact]
    public void AddMonthsClamped_AfterClampedFebruary_RestoresThirtyFirstInMarch() =>
        Assert.Equal(new DateOnly(2026, 3, 31), ReminderSchedule.AddMonthsClamped(new DateOnly(2026, 2, 28), 1, 31));

    [Fact]
    public void AddMonthsClamped_JanuaryThirtyFirstIntoLeapFebruary_ClampsToTwentyNine() =>
        Assert.Equal(new DateOnly(2028, 2, 29), ReminderSchedule.AddMonthsClamped(new DateOnly(2028, 1, 31), 1, 31));

    [Fact]
    public void AddMonthsClamped_AnnualFebruaryTwentyNinthIntoNonLeapYear_ClampsToTwentyEight() =>
        Assert.Equal(new DateOnly(2029, 2, 28), ReminderSchedule.AddMonthsClamped(new DateOnly(2028, 2, 29), 12, 29));

    [Fact]
    public void AddMonthsClamped_AnnualFebruaryTwentyNinthAfterTwoNonLeapYears_RestoresOnNextLeapYear() =>
        Assert.Equal(new DateOnly(2032, 2, 29), ReminderSchedule.AddMonthsClamped(new DateOnly(2031, 2, 28), 12, 29));
}
