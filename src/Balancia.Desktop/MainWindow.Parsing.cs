using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static Money ParseMoney(TextBox input) => Money.FromFrancs(decimal.Parse(input.Text ?? "", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture));
    private static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);
    private static void NormalizeAmount(TextBox input)
    {
        if (TryEvaluateAmount(input.Text, out var value))
        {
            input.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private static bool TryEvaluateAmount(string? text, out decimal value)
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
        catch (DivideByZeroException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static DateOnly ParseDate(CalendarDatePicker input) => input.SelectedDate is { } date
        ? DateOnly.FromDateTime(date)
        : throw new FormatException("Select a date.");

    private static string AmountText(Money value) => value.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
}
