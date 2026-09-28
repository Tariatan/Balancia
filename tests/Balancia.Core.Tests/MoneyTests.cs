using System.Globalization;
using Xunit;

namespace Balancia.Core.Tests;

public class MoneyTests
{
    [Fact]
    public void DecimalAdditionIsExact() => Assert.Equal(new Money(30), Money.FromFrancs(0.10m) + Money.FromFrancs(0.20m));

    [Theory]
    [InlineData("0")]
    [InlineData("-123.45")]
    [InlineData("92233720368547758.07")]
    [InlineData("-92233720368547758.08")]
    public void SupportedAmountsRoundTrip(string text)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Equal(value, Money.FromFrancs(value).Francs);
    }

    [Fact]
    public void FractionalCentimesAreRejected() => Assert.Throws<ArgumentException>(() => Money.FromFrancs(0.001m));

    [Fact]
    public void ConversionOverflowIsRejected() => Assert.Throws<OverflowException>(() => Money.FromFrancs(92233720368547758.08m));

    [Fact]
    public void ArithmeticOverflowIsRejected()
    {
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
        Assert.Throws<OverflowException>(() => new Money(long.MinValue) - new Money(1));
    }

    [Fact]
    public void UnaryMinusNegatesCentimes() => Assert.Equal(new Money(-30), -new Money(30));

    [Fact]
    public void UnaryMinusOverflowIsRejected() =>
        Assert.Throws<OverflowException>(() => -new Money(long.MinValue));

    [Theory]
    [InlineData(30, 30)]
    [InlineData(-30, 30)]
    [InlineData(0, 0)]
    public void AbsReturnsMagnitude(long centimes, long expected) => Assert.Equal(new Money(expected), new Money(centimes).Abs());

    [Fact]
    public void ComparisonOperatorsOrderByCentimes()
    {
        var small = new Money(10);
        var sameAsSmall = new Money(10);
        var large = new Money(20);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= sameAsSmall);
        Assert.True(small >= sameAsSmall);
        Assert.False(large < small);
        Assert.False(small > large);
    }

    [Fact]
    public void CompareToOrdersByCentimes()
    {
        Assert.True(new Money(10).CompareTo(new Money(20)) < 0);
        Assert.True(new Money(20).CompareTo(new Money(10)) > 0);
        Assert.Equal(0, new Money(10).CompareTo(new Money(10)));
    }
}
