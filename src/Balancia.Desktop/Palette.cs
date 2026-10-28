using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>
/// The colors of the application by role. Code that needs a color names its role here and never writes a hex value, so a color is defined
/// once. Each role is one shared brush.
/// </summary>
internal static class Palette
{
    private static readonly Dictionary<string, SolidColorBrush> tokens = [];
    private static readonly Dictionary<string, Color> builtInColors = [];

    // Text
    internal static readonly SolidColorBrush Text = Define(nameof(Text), "#263C48");
    internal static readonly SolidColorBrush TextMuted = Define(nameof(TextMuted), "#71838D");
    internal static readonly SolidColorBrush TextBody = Define(nameof(TextBody), "#344D44");
    internal static readonly SolidColorBrush Error = Define(nameof(Error), "#8B0000");

    // Accent and what sits on it
    internal static readonly SolidColorBrush Accent = Define(nameof(Accent), "#3989A7");
    internal static readonly SolidColorBrush AccentSurface = Define(nameof(AccentSurface), "#E7F2F6");
    internal static readonly SolidColorBrush AccentTintStrong = Define(nameof(AccentTintStrong), "#403989A7");
    internal static readonly SolidColorBrush AccentSurfaceHover = Define(nameof(AccentSurfaceHover), "#D5EAF1");
    internal static readonly SolidColorBrush AccentSurfacePressed = Define(nameof(AccentSurfacePressed), "#BDDDE9");
    internal static readonly SolidColorBrush AccentTintLight = Define(nameof(AccentTintLight), "#263989A7");
    internal static readonly SolidColorBrush AccentTintMedium = Define(nameof(AccentTintMedium), "#303989A7");
    internal static readonly SolidColorBrush OnAccent = Define(nameof(OnAccent), "#FFFFFF");

    // Meaning of an amount or a date
    internal static readonly SolidColorBrush Positive = Define(nameof(Positive), "#2C8B6D");
    internal static readonly SolidColorBrush Negative = Define(nameof(Negative), "#B95D4D");
    internal static readonly SolidColorBrush Warning = Define(nameof(Warning), "#B58B00");
    internal static readonly SolidColorBrush Transfer = Define(nameof(Transfer), "#1B4F72");

    // Surfaces
    internal static readonly SolidColorBrush WindowBackground = Define(nameof(WindowBackground), "#F6F8FA");
    internal static readonly SolidColorBrush Surface = Define(nameof(Surface), "#FFFFFF");
    internal static readonly SolidColorBrush Border = Define(nameof(Border), "#DCE6EB");
    internal static readonly SolidColorBrush HeaderBar = Define(nameof(HeaderBar), "#1B303D");
    internal static readonly SolidColorBrush OnHeaderBar = Define(nameof(OnHeaderBar), "#FFFFFF");
    internal static readonly SolidColorBrush RowBlue = Define(nameof(RowBlue), "#F3F8FA");
    internal static readonly SolidColorBrush RowIvory = Define(nameof(RowIvory), "#FCF8F0");
    internal static readonly SolidColorBrush SelectionBackground = Define(nameof(SelectionBackground), "#D8ECF3");
    internal static readonly SolidColorBrush SelectionForeground = Define(nameof(SelectionForeground), "#1C627E");
    internal static readonly SolidColorBrush PopupBackground = Define(nameof(PopupBackground), "#E5E5E5");
    internal static readonly SolidColorBrush PopupBorder = Define(nameof(PopupBorder), "#BFC7CB");
    internal static readonly SolidColorBrush SuggestionTopMatch = Define(nameof(SuggestionTopMatch), "#52636C");

    // Charts
    internal static readonly SolidColorBrush ChartIncome = Define(nameof(ChartIncome), "#2C8B6D");
    internal static readonly SolidColorBrush ChartExpense = Define(nameof(ChartExpense), "#EF8072");
    internal static readonly SolidColorBrush ChartSavings = Define(nameof(ChartSavings), "#289FCC");
    internal static readonly SolidColorBrush ChartPrevious = Define(nameof(ChartPrevious), "#B8C2CC");
    internal static readonly SolidColorBrush ChartAxis = Define(nameof(ChartAxis), "#CBD5DC");
    internal static readonly SolidColorBrush ChartLabel = Define(nameof(ChartLabel), "#71838D");

    /// <summary>Every role by name, in the order declared.</summary>
    internal static IReadOnlyDictionary<string, SolidColorBrush> Tokens => tokens;

    /// <summary>The resource key XAML uses for a role: <c>{DynamicResource Palette.Text}</c>.</summary>
    internal static string ResourceKey(string name) => "Palette." + name;

    /// <summary>Makes every role available to XAML as an application resource.</summary>
    internal static void Register(IResourceDictionary resources)
    {
        foreach (var (name, brush) in tokens)
        {
            resources[ResourceKey(name)] = brush;
        }
    }

    /// <summary>
    /// Shows <paramref name="overrides"/> (role name to color) and the built-in color of every role it does not mention. The shared brushes keep
    /// their identity and change color, so every control that holds one follows. Call it on the UI thread.
    /// </summary>
    internal static void Apply(IReadOnlyDictionary<string, Color> overrides)
    {
        foreach (var (name, brush) in tokens)
        {
            var color = overrides.TryGetValue(name, out var chosen) ? chosen : builtInColors[name];
            if (brush.Color != color)
            {
                brush.Color = color;
            }
        }
    }

    /// <summary>Positive above zero, negative at zero or below.</summary>
    internal static IBrush ForBalance(Money amount) => amount > Money.Zero ? Positive : Negative;

    private static SolidColorBrush Define(string name, string hex)
    {
        var color = Color.Parse(hex);
        var brush = new SolidColorBrush(color);
        tokens.Add(name, brush);
        builtInColors.Add(name, color);
        return brush;
    }
}
