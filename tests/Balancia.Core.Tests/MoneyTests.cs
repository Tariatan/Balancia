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
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
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
}
