using Xunit;

namespace Balancia.Core.Tests;

public class FlowAnalyticsTests
{
    [Fact]
    public void Trend_Week_SplitsIntoCalendarWeeksAlignedToMonday()
    {
        // Range spans a partial week (Thu-Sun), a full week (Mon-Sun), and a partial week (Mon-Tue).
        var from = new DateOnly(2026, 1, 1); // Thursday
        var to = new DateOnly(2026, 1, 13); // Tuesday
        var current = new[]
        {
            new DailyFlow(new DateOnly(2026, 1, 1), Money.FromFrancs(10), Money.Zero),
            new DailyFlow(new DateOnly(2026, 1, 5), Money.FromFrancs(20), Money.Zero), // Monday of full week
            new DailyFlow(new DateOnly(2026, 1, 13), Money.FromFrancs(30), Money.Zero),
        };
        var analytics = new FlowAnalytics(from, to, null, null, current, []);

        var buckets = analytics.Trend(FlowInterval.Week);

        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), buckets[0].From);
        Assert.Equal(new DateOnly(2026, 1, 4), buckets[0].To);
        Assert.Equal(Money.FromFrancs(10), buckets[0].Income);
        Assert.Equal(new DateOnly(2026, 1, 5), buckets[1].From);
        Assert.Equal(new DateOnly(2026, 1, 11), buckets[1].To);
        Assert.Equal(Money.FromFrancs(20), buckets[1].Income);
        Assert.Equal(new DateOnly(2026, 1, 12), buckets[2].From);
        Assert.Equal(new DateOnly(2026, 1, 13), buckets[2].To);
        Assert.Equal(Money.FromFrancs(30), buckets[2].Income);
    }

    [Fact]
    public void Trend_SingleDayRange_ReturnsOneBucketForEveryInterval()
    {
        var date = new DateOnly(2026, 1, 5);
        var analytics = new FlowAnalytics(date, date, null, null, [], []);

        Assert.Single(analytics.Trend(FlowInterval.Day));
        Assert.Single(analytics.Trend(FlowInterval.Week));
        Assert.Single(analytics.Trend(FlowInterval.Month));
    }

    [Fact]
    public void FlowBucket_Savings_IsIncomeMinusExpenses() =>
        Assert.Equal(Money.FromFrancs(-5), new FlowBucket(default, default, Money.FromFrancs(10), Money.FromFrancs(15)).Savings);
}
