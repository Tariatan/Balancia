using System.Globalization;
using Xunit;

namespace Balancia.Core.Tests;

public class MoneyTests
{
    [Fact]
    public void Add_DecimalFrancs_IsExact()
    {
        // Arrange
        var first = Money.FromFrancs(0.10m);
        var second = Money.FromFrancs(0.20m);

        // Act
        var sum = first + second;

        // Assert
        Assert.Equal(new Money(30), sum);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-123.45")]
    [InlineData("92233720368547758.07")]
    [InlineData("-92233720368547758.08")]
    public void FromFrancs_SupportedAmount_RoundTrips(string text)
    {
        // Arrange
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);

        // Act
        var francs = Money.FromFrancs(value).Francs;

        // Assert
        Assert.Equal(value, francs);
    }

    [Fact]
    public void FromFrancs_FractionalCentimes_ThrowsArgumentException()
    {
        // Act
        Action convert = () => _ = Money.FromFrancs(0.001m);

        // Assert
        Assert.Throws<ArgumentException>(convert);
    }

    [Fact]
    public void FromFrancs_AboveMaximum_ThrowsOverflowException()
    {
        // Act
        Action convert = () => _ = Money.FromFrancs(92233720368547758.08m);

        // Assert
        Assert.Throws<OverflowException>(convert);
    }

    [Fact]
    public void Add_PastMaximum_ThrowsOverflowException()
    {
        // Arrange
        var maximum = new Money(long.MaxValue);

        // Act
        Action add = () => _ = maximum + new Money(1);

        // Assert
        Assert.Throws<OverflowException>(add);
    }

    [Fact]
    public void Subtract_PastMinimum_ThrowsOverflowException()
    {
        // Arrange
        var minimum = new Money(long.MinValue);

        // Act
        Action subtract = () => _ = minimum - new Money(1);

        // Assert
        Assert.Throws<OverflowException>(subtract);
    }

    [Fact]
    public void Negate_PositiveAmount_ReturnsNegativeAmount()
    {
        // Arrange
        var amount = new Money(30);

        // Act
        var result = -amount;

        // Assert
        Assert.Equal(new Money(-30), result);
    }

    [Fact]
    public void Negate_MinimumAmount_ThrowsOverflowException()
    {
        // Arrange
        var minimum = new Money(long.MinValue);

        // Act
        Action negate = () => _ = -minimum;

        // Assert
        Assert.Throws<OverflowException>(negate);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(-30, 30)]
    [InlineData(0, 0)]
    public void Abs_AnyAmount_ReturnsMagnitude(long centimes, long expected)
    {
        // Arrange
        var amount = new Money(centimes);

        // Act
        var result = amount.Abs();

        // Assert
        Assert.Equal(new Money(expected), result);
    }

    [Theory]
    [InlineData(10, 20, true, false, true, false)]
    [InlineData(20, 10, false, true, false, true)]
    [InlineData(10, 10, false, false, true, true)]
    public void ComparisonOperators_TwoAmounts_OrderByCentimes(
        long left, long right, bool less, bool greater, bool lessOrEqual, bool greaterOrEqual)
    {
        // Arrange
        var leftAmount = new Money(left);
        var rightAmount = new Money(right);

        // Act
        var results = (IsLess: leftAmount < rightAmount, IsGreater: leftAmount > rightAmount, IsLessOrEqual: leftAmount <= rightAmount, IsGreaterOrEqual: leftAmount >= rightAmount);

        // Assert
        Assert.Equal((less, greater, lessOrEqual, greaterOrEqual), results);
    }

    [Theory]
    [InlineData(10, 20, -1)]
    [InlineData(20, 10, 1)]
    [InlineData(10, 10, 0)]
    public void CompareTo_TwoAmounts_ReturnsSignOfCentimeDifference(long left, long right, int expectedSign)
    {
        // Arrange
        var leftAmount = new Money(left);
        var rightAmount = new Money(right);

        // Act
        var result = leftAmount.CompareTo(rightAmount);

        // Assert
        Assert.Equal(expectedSign, Math.Sign(result));
    }
}
