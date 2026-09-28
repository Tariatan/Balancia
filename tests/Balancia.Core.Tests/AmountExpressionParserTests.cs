using Xunit;

namespace Balancia.Core.Tests;

public class AmountExpressionParserTests
{
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("  12.50  ", 12.50)]
    [InlineData("1+2", 3)]
    [InlineData("10-2.5", 7.5)]
    [InlineData("2*3", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("-5", -5)]
    [InlineData("--5", 5)]
    [InlineData("-(2+3)", -5)]
    [InlineData("+5", 5)]
    public void Parse_ValidExpressions_ReturnsComputedValue(string text, decimal expected) =>
        Assert.Equal(expected, new AmountExpressionParser(text).Parse());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("1 2")]
    [InlineData("1.2.3")]
    public void Parse_MalformedExpressions_ThrowsFormatException(string text) =>
        Assert.Throws<FormatException>(() => new AmountExpressionParser(text).Parse());

    [Fact]
    public void Parse_DivisionByZero_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => new AmountExpressionParser("1/0").Parse());

    [Fact]
    public void TryEvaluate_ValidExpression_RoundsToCentimesAwayFromZero()
    {
        var evaluated = AmountExpressionParser.TryEvaluate("1/3", out var value);

        Assert.True(evaluated);
        Assert.Equal(0.33m, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1/0")]
    public void TryEvaluate_InvalidInput_ReturnsFalseWithoutThrowing(string? text)
    {
        var evaluated = AmountExpressionParser.TryEvaluate(text, out var value);

        Assert.False(evaluated);
        Assert.Equal(0, value);
    }
}
