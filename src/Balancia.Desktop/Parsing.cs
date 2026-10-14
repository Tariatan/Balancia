using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static Money ParseMoney(TextBox input)
        => Money.FromFrancs(
            decimal.Parse(input.Text ?? "",
                NumberStyles.AllowLeadingSign
                | NumberStyles.AllowDecimalPoint
                | NumberStyles.AllowLeadingWhite
                | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture));
    private static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);
    private static void NormalizeAmount(TextBox input)
    {
        if (AmountExpressionParser.TryEvaluate(input.Text, out var value))
        {
            input.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private static DateOnly ParseDate(CalendarDatePicker input) => input.SelectedDate is { } date
        ? DateOnly.FromDateTime(date)
        : throw new FormatException("Select a date.");

    private static string AmountText(Money value) => value.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
}
