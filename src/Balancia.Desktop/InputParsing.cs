using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;

namespace Balancia.Desktop;

internal static class InputParsing
{
    private const NumberStyles MoneyInputStyles = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite;

    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("de-CH");

    internal static Money ParseMoney(TextBox input)
        => Money.FromFrancs(decimal.Parse(input.Text ?? string.Empty, MoneyInputStyles, CultureInfo.InvariantCulture));

    internal static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);

    internal static Money ParseAmountExpression(TextBox input)
    {
        var expression = (input.Text ?? string.Empty).Replace(',', '.');
        var total = Money.Zero;
        foreach (var term in expression.AsSpan().SplitAny("+-"))
        {
            var normalized = expression[term];
            // Validate every operand independently: no unary signs, parentheses or other syntax.
            foreach (var operand in normalized.Split('*', '/'))
            {
                _ = decimal.Parse(operand, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }

            var amount = Money.FromFrancs(new AmountExpressionParser(normalized).Parse());
            var start = term.Start.Value;
            total = start > 0 && expression[start - 1] == '-' ? total - amount : total + amount;
        }

        return total;
    }

    internal static void NormalizeAmountExpression(TextBox input)
    {
        if (input.Text is not { } text || text.IndexOfAny(['+', '-', '*', '/']) < 0)
        {
            return;
        }

        try
        {
            var amount = ParseAmountExpression(input);
            if (amount >= Money.Zero)
            {
                input.Text = amount.Francs.ToString("0.00", Localization.UiText.Culture);
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            // Keep invalid or unfinished expressions visible so save validation can report them.
        }
    }

    internal static DateOnly ParseDate(CalendarDatePicker input) => input.SelectedDate is { } date
        ? DateOnly.FromDateTime(date)
        : throw new FormatException("Select a date.");

    internal static string AmountText(Money value) => value.Francs.ToString("N2", DisplayCulture);
}
