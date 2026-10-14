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
    public void Parse_NestingAtLimit_ReturnsValue() =>
        Assert.Equal(1m, new AmountExpressionParser(Nested(64)).Parse());

    [Fact]
    public void Parse_NestingBeyondLimit_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => new AmountExpressionParser(Nested(65)).Parse());

    [Fact]
    public void Parse_LongUnaryMinusChain_AppliesParityWithoutStackGrowth()
    {
        Assert.Equal(5m, new AmountExpressionParser(new string('-', 100_000) + "5").Parse());
        Assert.Equal(-5m, new AmountExpressionParser(new string('-', 100_001) + "5").Parse());
    }

    [Fact]
    public void Parse_MultiplicationOverflow_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => new AmountExpressionParser("79228162514264337593543950335*2").Parse());

    private static string Nested(int depth) => new string('(', depth) + "1" + new string(')', depth);

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
