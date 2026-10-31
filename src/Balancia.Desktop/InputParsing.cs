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

    internal static DateOnly ParseDate(CalendarDatePicker input) => input.SelectedDate is { } date
        ? DateOnly.FromDateTime(date)
        : throw new FormatException("Select a date.");

    internal static string AmountText(Money value) => value.Francs.ToString("N2", DisplayCulture);
}
