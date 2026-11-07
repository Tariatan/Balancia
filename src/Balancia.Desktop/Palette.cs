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
    private static readonly Dictionary<string, Color> glacierColors = [];

    private static readonly Dictionary<string, Color> graphiteColors = [];

    // Text
    internal static readonly SolidColorBrush Text = Define(nameof(Text), "#263D4C", "#E6EAF0");
    internal static readonly SolidColorBrush TextMuted = Define(nameof(TextMuted), "#607889", "#A0ADBD");
    internal static readonly SolidColorBrush TextBody = Define(nameof(TextBody), "#263D4C", "#E6EAF0");
    internal static readonly SolidColorBrush Error = Define(nameof(Error), "#BB4B3D", "#FF9A8D");

    // Accent and what sits on it
    internal static readonly SolidColorBrush Accent = Define(nameof(Accent), "#277FA5", "#8AB8FF");
    internal static readonly SolidColorBrush AccentSurface = Define(nameof(AccentSurface), "#DCEEF5", "#2C405C");
    internal static readonly SolidColorBrush AccentTintStrong = Define(nameof(AccentTintStrong), "#40277FA5", "#408AB8FF");
    internal static readonly SolidColorBrush AccentSurfaceHover = Define(nameof(AccentSurfaceHover), "#DCEEF5", "#2C405C");
    internal static readonly SolidColorBrush AccentSurfacePressed = Define(nameof(AccentSurfacePressed), "#D6E3EC", "#343E4A");
    internal static readonly SolidColorBrush AccentTintLight = Define(nameof(AccentTintLight), "#26277FA5", "#268AB8FF");
    internal static readonly SolidColorBrush AccentTintMedium = Define(nameof(AccentTintMedium), "#30277FA5", "#308AB8FF");
    internal static readonly SolidColorBrush OnAccent = Define(nameof(OnAccent), "#FFFFFF", "#172130");

    // Meaning of an amount or a date
    internal static readonly SolidColorBrush Positive = Define(nameof(Positive), "#187B67", "#79D7B0");
    internal static readonly SolidColorBrush Negative = Define(nameof(Negative), "#BB4B3D", "#FF9A8D");
    internal static readonly SolidColorBrush Warning = Define(nameof(Warning), "#946900", "#E7B75A");
    internal static readonly SolidColorBrush Transfer = Define(nameof(Transfer), "#277FA5", "#8AB8FF");

    // Surfaces
    internal static readonly SolidColorBrush WindowBackground = Define(nameof(WindowBackground), "#F0F5F9", "#15191F");
    internal static readonly SolidColorBrush Surface = Define(nameof(Surface), "#FFFFFF", "#20262E");
    internal static readonly SolidColorBrush Border = Define(nameof(Border), "#D6E3EC", "#343E4A");
    internal static readonly SolidColorBrush HeaderBar = Define(nameof(HeaderBar), "#1D3543", "#15191F");
    internal static readonly SolidColorBrush OnHeaderBar = Define(nameof(OnHeaderBar), "#FFFFFF", "#E6EAF0");
    internal static readonly SolidColorBrush RowBlue = Define(nameof(RowBlue), "#F3F8FB", "#252D37");
    internal static readonly SolidColorBrush RowIvory = Define(nameof(RowIvory), "#FFFFFF", "#20262E");
    internal static readonly SolidColorBrush SelectionBackground = Define(nameof(SelectionBackground), "#DCEEF5", "#2C405C");
    internal static readonly SolidColorBrush SelectionForeground = Define(nameof(SelectionForeground), "#263D4C", "#E6EAF0");
    internal static readonly SolidColorBrush PopupBackground = Define(nameof(PopupBackground), "#FFFFFF", "#20262E");
    internal static readonly SolidColorBrush PopupBorder = Define(nameof(PopupBorder), "#D6E3EC", "#343E4A");
    internal static readonly SolidColorBrush SuggestionTopMatch = Define(nameof(SuggestionTopMatch), "#607889", "#A0ADBD");

    // Charts
    internal static readonly SolidColorBrush ChartIncome = Define(nameof(ChartIncome), "#187B67", "#79D7B0");
    internal static readonly SolidColorBrush ChartExpense = Define(nameof(ChartExpense), "#BB4B3D", "#FF9A8D");
    internal static readonly SolidColorBrush ChartSavings = Define(nameof(ChartSavings), "#277FA5", "#8AB8FF");
    internal static readonly SolidColorBrush ChartPrevious = Define(nameof(ChartPrevious), "#607889", "#A0ADBD");
    internal static readonly SolidColorBrush ChartAxis = Define(nameof(ChartAxis), "#D6E3EC", "#343E4A");
    internal static readonly SolidColorBrush ChartLabel = Define(nameof(ChartLabel), "#607889", "#A0ADBD");

    internal static IReadOnlyDictionary<string, Color> GraphiteColors => graphiteColors;

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
        FluentPalette.Register(resources);
    }

    /// <summary>
    /// Shows <paramref name="overrides"/> (role name to color) and the Glacier color of every role it does not mention. The shared brushes keep
    /// their identity and change color, so every control that holds one follows. Call it on the UI thread.
    /// </summary>
    internal static void Apply(IReadOnlyDictionary<string, Color> overrides)
    {
        foreach (var (name, brush) in tokens)
        {
            var color = overrides.TryGetValue(name, out var chosen) ? chosen : glacierColors[name];
            if (brush.Color != color)
            {
                brush.Color = color;
            }
        }
    }

    /// <summary>Positive above zero, negative at zero or below.</summary>
    internal static IBrush ForBalance(Money amount) => amount > Money.Zero ? Positive : Negative;

    private static SolidColorBrush Define(string name, string glacier, string graphite)
    {
        var color = Color.Parse(glacier);
        var brush = new SolidColorBrush(color);
        tokens.Add(name, brush);
        glacierColors.Add(name, color);
        graphiteColors.Add(name, Color.Parse(graphite));
        return brush;
    }
}
