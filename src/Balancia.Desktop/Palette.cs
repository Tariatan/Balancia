using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>
/// The colors of the application by role. Code that needs a color names its role here and never writes a hex value, so a color is defined
/// once. Each role is one shared brush.
/// </summary>
internal static class Palette
{
    // Text
    internal static readonly SolidColorBrush Text = Define("#263C48");
    internal static readonly SolidColorBrush TextMuted = Define("#71838D");
    internal static readonly SolidColorBrush TextBody = Define("#344D44");

    // Accent and what sits on it
    internal static readonly SolidColorBrush Accent = Define("#3989A7");
    internal static readonly SolidColorBrush AccentSurface = Define("#E7F2F6");
    internal static readonly SolidColorBrush AccentTintStrong = Define("#403989A7");
    internal static readonly SolidColorBrush OnAccent = Define("#FFFFFF");

    // Meaning of an amount or a date
    internal static readonly SolidColorBrush Positive = Define("#2C8B6D");
    internal static readonly SolidColorBrush Negative = Define("#B95D4D");
    internal static readonly SolidColorBrush Warning = Define("#B58B00");
    internal static readonly SolidColorBrush Transfer = Define("#1B4F72");

    // Surfaces
    internal static readonly SolidColorBrush Surface = Define("#FFFFFF");
    internal static readonly SolidColorBrush RowBlue = Define("#F3F8FA");
    internal static readonly SolidColorBrush RowIvory = Define("#FCF8F0");
    internal static readonly SolidColorBrush SelectionBackground = Define("#D8ECF3");
    internal static readonly SolidColorBrush SelectionForeground = Define("#1C627E");
    internal static readonly SolidColorBrush PopupBackground = Define("#E5E5E5");
    internal static readonly SolidColorBrush PopupBorder = Define("#BFC7CB");
    internal static readonly SolidColorBrush SuggestionTopMatch = Define("#52636C");

    // Charts
    internal static readonly SolidColorBrush ChartIncome = Define("#2C8B6D");
    internal static readonly SolidColorBrush ChartExpense = Define("#EF8072");
    internal static readonly SolidColorBrush ChartSavings = Define("#289FCC");
    internal static readonly SolidColorBrush ChartPrevious = Define("#B8C2CC");
    internal static readonly SolidColorBrush ChartAxis = Define("#CBD5DC");
    internal static readonly SolidColorBrush ChartLabel = Define("#71838D");

    /// <summary>Positive above zero, negative at zero or below.</summary>
    internal static IBrush ForBalance(Money amount) => amount > Money.Zero ? Positive : Negative;

    private static SolidColorBrush Define(string hex) => new(Color.Parse(hex));
}
