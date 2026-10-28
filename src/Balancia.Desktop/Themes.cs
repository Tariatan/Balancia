using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<ColorTheme> themes = [];

    /// <summary>The themes the owner can choose from: the built-in one first, then the files of the <c>themes</c> folder beside the settings file.</summary>
    internal IReadOnlyList<ColorTheme> Themes => [ColorTheme.BuiltIn, .. themes];

    /// <summary>The name of the theme in use.</summary>
    internal string ThemeName { get; private set; } = ColorTheme.BuiltInName;

    private string ThemesDirectory => Path.Combine(Path.GetDirectoryName(settingsStore.Path)!, "themes");

    /// <summary>
    /// Reads the theme files and shows the saved theme, or the built-in one when none was saved or its file is gone. A separate data folder
    /// never reads the user's files, so it only has the built-in theme.
    /// </summary>
    private void LoadThemes()
    {
        themes = separateDataFolder ? [] : ColorThemeCatalog.Load(ThemesDirectory);
        ShowTheme(Find(settings.Theme));
    }

    /// <summary>Shows the theme named <paramref name="name"/> at once and remembers it; the built-in theme is remembered as no choice.</summary>
    internal void ApplyThemeChange(string name)
    {
        var theme = Find(name);
        var updated = settings with { Theme = theme.Name == ColorTheme.BuiltInName ? null : theme.Name };
        PersistSettings(updated);
        settings = updated;
        ShowTheme(theme);
        Serilog.Log.Information("Application theme changed, Theme: '{Theme}'", theme.Name);
    }

    private ColorTheme Find(string? name) =>
        themes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? ColorTheme.BuiltIn;

    /// <summary>Changes the palette colors, then redraws every open window, because what is drawn by hand does not follow a brush on its own.</summary>
    private void ShowTheme(ColorTheme theme)
    {
        ThemeName = theme.Name;
        Palette.Apply(theme.Colors);
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
