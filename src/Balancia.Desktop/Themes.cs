using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<ColorTheme> themes = [];

    /// <summary>The themes the owner can choose from: Glacier and Graphite, followed by the files of the <c>themes</c> folder beside the settings file.</summary>
    internal IReadOnlyList<ColorTheme> Themes => [ColorTheme.Glacier, ColorTheme.Graphite, .. themes];

    /// <summary>The name of the theme in use.</summary>
    internal string ThemeName { get; private set; } = ColorTheme.GlacierName;

    private string ThemesDirectory => Path.Combine(Path.GetDirectoryName(settingsStore.Path)!, "themes");

    /// <summary>
    /// Shows the saved theme, falling back to Glacier. A separate data folder offers only the two built-in themes.
    /// </summary>
    private void LoadThemes()
    {
        themes = separateDataFolder ? [] : ColorThemeCatalog.Load(ThemesDirectory);
        ShowTheme(Find(settings.Theme));
    }

    /// <summary>Shows the theme named <paramref name="name"/> at once and remembers it; Glacier is remembered as no choice.</summary>
    internal void ApplyThemeChange(string name)
    {
        var theme = Find(name);
        var updated = settings with { Theme = theme.Name == ColorTheme.GlacierName ? null : theme.Name };
        PersistSettings(updated);
        settings = updated;
        ShowTheme(theme);
        Serilog.Log.Information("Application theme changed, Theme: '{Theme}'", theme.Name);
    }

    private Task ToggleTheme() => operations.RunAsync(() =>
    {
        ApplyThemeChange(ThemeName == ColorTheme.GlacierName ? ColorTheme.GraphiteName : ColorTheme.GlacierName);
        return Task.CompletedTask;
    }, disableControls: false, operationName: nameof(ToggleTheme));

    private ColorTheme Find(string? name) =>
        Themes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? ColorTheme.Glacier;

    /// <summary>Changes the palette colors, then redraws every open window, because what is drawn by hand does not follow a brush on its own.</summary>
    private void ShowTheme(ColorTheme theme)
    {
        ThemeName = theme.Name;
        Palette.Apply(theme.Colors);
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme == ColorTheme.Graphite ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [this];
        foreach (var window in windows)
        {
            window.InvalidateVisual();
            foreach (var visual in window.GetVisualDescendants())
            {
                visual.InvalidateVisual();
            }
        }
    }
}
