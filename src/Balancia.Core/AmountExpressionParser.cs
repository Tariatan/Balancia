using System.Globalization;
using Serilog;

namespace Balancia.Core;

public sealed class AmountExpressionParser(string text)
{
    private const int MaxNestingDepth = 64;

    private int index;
    private int depth;

    public decimal Parse()
    {
        try
        {
            var value = ParseExpression();
            SkipWhitespace();

            return index == text.Length ? value : throw new FormatException();
        }
        catch (Exception exception) when (exception is DivideByZeroException or OverflowException)
        {
            throw new FormatException(null, exception);
        }
    }

    public static bool TryEvaluate(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            value = decimal.Round(new AmountExpressionParser(text).Parse(), 2, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (FormatException exception)
        {
            Log.ForContext<AmountExpressionParser>().Debug("Amount expression rejected, FailureType: '{FailureType}'", exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            return false;
        }
    }

    private decimal ParseExpression()
    {
        var value = ParseTerm();
        while (true)
        {
            SkipWhitespace();
            if (Match('+'))
            {
                value += ParseTerm();
            }
            else if (Match('-'))
            {
                value -= ParseTerm();
            }
            else
            {
                return value;
            }
        }
    }

    private decimal ParseTerm()
    {
        var value = ParseUnary();
        while (true)
        {
            SkipWhitespace();
            if (Match('*'))
            {
                value *= ParseUnary();
            }
            else if (Match('/'))
            {
                value /= ParseUnary();
            }
            else
            {
                return value;
            }
        }
    }

    private decimal ParseUnary()
    {
        var negative = false;
        while (true)
        {
            SkipWhitespace();
            if (Match('+'))
            {
                continue;
            }

            if (!Match('-'))
            {
                break;
            }

            negative = !negative;
        }

        var value = ParsePrimary();

        return negative ? -value : value;
    }

    private decimal ParsePrimary()
    {
        if (!Match('('))
        {
            return ParseNumber();
        }

        if (++depth > MaxNestingDepth)
        {
            throw new FormatException();
        }

        var value = ParseExpression();
        SkipWhitespace();
        depth--;

        return Match(')') ? value : throw new FormatException();
    }

    private decimal ParseNumber()
    {
        var start = index;
        var hasDigits = false;
        var hasDecimalPoint = false;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                hasDigits = true;
            }
            else if (character != '.' || hasDecimalPoint)
            {
                break;
            }
            else
            {
                hasDecimalPoint = true;
            }

            index++;
        }

        return hasDigits
            ? decimal.Parse(text.AsSpan(start, index - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : throw new FormatException();
    }

    private bool Match(char character)
    {
        if (index >= text.Length || text[index] != character)
        {
            return false;
        }

        index++;
        return true;
    }

    private void SkipWhitespace()
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }
}
