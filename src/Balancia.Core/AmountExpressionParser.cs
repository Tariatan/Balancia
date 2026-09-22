using System.Globalization;

namespace Balancia.Core;

public sealed class AmountExpressionParser(string text)
{
    private int index;

    public decimal Parse()
    {
        var value = ParseExpression();
        SkipWhitespace();
        return index == text.Length ? value : throw new FormatException();
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
        while (true)
        {
            SkipWhitespace();
            if (Match('+'))
            {
                continue;
            }

            if (Match('-'))
            {
                return -ParseUnary();
            }

            return ParsePrimary();
        }
    }

    private decimal ParsePrimary()
    {
        SkipWhitespace();
        if (!Match('('))
        {
            return ParseNumber();
        }

        var value = ParseExpression();
        SkipWhitespace();

        return Match(')') ? value : throw new FormatException();
    }

    private decimal ParseNumber()
    {
        SkipWhitespace();
        var start = index;
        var hasDigits = false;
        var hasDecimalPoint = false;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsDigit(character))
            {
                hasDigits = true;
                index++;
                continue;
            }

            if (character == '.' && !hasDecimalPoint)
            {
                hasDecimalPoint = true;
                index++;
                continue;
            }

            break;
        }

        return hasDigits
            ? decimal.Parse(text[start..index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
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
