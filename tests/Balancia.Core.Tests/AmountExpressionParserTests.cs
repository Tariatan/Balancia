using Xunit;

namespace Balancia.Core.Tests;

public class AmountExpressionParserTests
{
    private const int MaxNesting = 64;

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
    public void Parse_ValidExpression_ReturnsComputedValue(string text, decimal expected)
    {
        // Arrange
        var parser = new AmountExpressionParser(text);

        // Act
        var result = parser.Parse();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("1 2")]
    [InlineData("1.2.3")]
    [InlineData("1/0")]
    [InlineData("79228162514264337593543950335*2")]
    public void Parse_InvalidExpression_ThrowsFormatException(string text)
    {
        // Arrange
        var parser = new AmountExpressionParser(text);

        // Act
        Action parse = () => parser.Parse();

        // Assert
        Assert.Throws<FormatException>(parse);
    }

    [Fact]
    public void Parse_NestingAtLimit_ReturnsValue()
    {
        // Arrange
        var parser = new AmountExpressionParser(Nested(MaxNesting));

        // Act
        var result = parser.Parse();

        // Assert
        Assert.Equal(1m, result);
    }

    [Fact]
    public void Parse_NestingBeyondLimit_ThrowsFormatException()
    {
        // Arrange
        var parser = new AmountExpressionParser(Nested(MaxNesting + 1));

        // Act
        Action parse = () => parser.Parse();

        // Assert
        Assert.Throws<FormatException>(parse);
    }

    [Theory]
    [InlineData(100_000, 5)]
    [InlineData(100_001, -5)]
    public void Parse_LongUnaryMinusChain_AppliesParityWithoutStackGrowth(int minusCount, decimal expected)
    {
        // Arrange
        var parser = new AmountExpressionParser(new string('-', minusCount) + "5");

        // Act
        var result = parser.Parse();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryEvaluate_ValidExpression_RoundsToCentimesAwayFromZero()
    {
        // Act
        var evaluated = AmountExpressionParser.TryEvaluate("1/3", out var value);

        // Assert
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
        // Act
        var evaluated = AmountExpressionParser.TryEvaluate(text, out var value);

        // Assert
        Assert.False(evaluated);
        Assert.Equal(0, value);
    }

    private static string Nested(int depth) => new string('(', depth) + "1" + new string(')', depth);
}
