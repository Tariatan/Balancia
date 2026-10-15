using Xunit;

namespace Balancia.Core.Tests;

public class FlowAnalyticsTests
{
    [Fact]
    public void Trend_Week_SplitsIntoCalendarWeeksAlignedToMonday()
    {
        // Arrange
        // Range spans a partial week (Thu-Sun), a full week (Mon-Sun), and a partial week (Mon-Tue).
        var from = new DateOnly(2026, 1, 1); // Thursday
        var to = new DateOnly(2026, 1, 13); // Tuesday
        DailyFlow[] current =
        [
            new(new DateOnly(2026, 1, 1), Money.FromFrancs(10), Money.Zero),
            new(new DateOnly(2026, 1, 5), Money.FromFrancs(20), Money.Zero), // Monday of full week
            new(new DateOnly(2026, 1, 13), Money.FromFrancs(30), Money.Zero),
        ];
        var analytics = new FlowAnalytics(from, to, null, null, current, []);

        // Act
        var buckets = analytics.Trend(FlowInterval.Week);

        // Assert
        Assert.Equal(
            [
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4), Money.FromFrancs(10)),
                (new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11), Money.FromFrancs(20)),
                (new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 13), Money.FromFrancs(30)),
            ],
            buckets.Select(bucket => (bucket.From, bucket.To, bucket.Income)));
    }

    [Fact]
    public void Trend_UndefinedInterval_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var date = new DateOnly(2026, 1, 5);
        var analytics = new FlowAnalytics(date, date, null, null, [], []);

        // Act
        var trend = () => analytics.Trend((FlowInterval)99);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(trend);
    }

    [Theory]
    [InlineData(FlowInterval.Day)]
    [InlineData(FlowInterval.Week)]
    [InlineData(FlowInterval.Month)]
    public void Trend_SingleDayRange_ReturnsOneBucket(FlowInterval interval)
    {
        // Arrange
        var date = new DateOnly(2026, 1, 5);
        var analytics = new FlowAnalytics(date, date, null, null, [], []);

        // Act
        var buckets = analytics.Trend(interval);

        // Assert
        Assert.Single(buckets);
    }

    [Fact]
    public void Savings_IncomeAndExpenses_IsIncomeMinusExpenses()
    {
        // Arrange
        var bucket = new FlowBucket(default, default, Money.FromFrancs(10), Money.FromFrancs(15));

        // Act
        var savings = bucket.Savings;

        // Assert
        Assert.Equal(Money.FromFrancs(-5), savings);
    }
}
