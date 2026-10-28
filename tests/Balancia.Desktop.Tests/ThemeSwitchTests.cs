using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>Theme files beside the settings file, chosen in Settings and shown at once.</summary>
public sealed class ThemeSwitchTests : IDisposable
{
    private const string Dusk = """{ "name": "Dusk", "colors": { "Text": "#E0E0E0", "WindowBackground": "#102030" } }""";

    // The palette is shared by every test, so each one ends by showing the built-in colors again.
    public void Dispose() => Palette.Apply(new Dictionary<string, Color>());

    private static void WriteTheme(DesktopWorkspace workspace, string fileName, string json)
    {
        Directory.CreateDirectory(workspace.ThemesDirectory);
        File.WriteAllText(Path.Combine(workspace.ThemesDirectory, fileName), json);
    }

    private static async Task<(MainWindow Window, SettingsWindow Settings)> OpenSettingsAsync(DesktopWorkspace workspace)
    {
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        window.Click("SettingsButton");
        return (window, await window.WaitForDialogAsync<SettingsWindow>());
    }

    [AvaloniaFact]
    public async Task Open_WithASavedTheme_StartsWithItsColors()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        workspace.WriteSettings(theme: "Dusk");

        // Act
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Assert
        Assert.Equal("Dusk", window.ThemeName);
        Assert.Equal(Color.Parse("#E0E0E0"), Palette.Text.Color);
        Assert.Equal(Color.Parse("#102030"), Palette.WindowBackground.Color);
        Assert.Same(Palette.WindowBackground, window.Background);
    }

    [AvaloniaFact]
    public async Task Open_WithASavedThemeWhoseFileIsGone_StartsBuiltIn()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(theme: "Dusk");

        // Act
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Assert
        Assert.Equal(ColorTheme.BuiltInName, window.ThemeName);
        Assert.Equal(Color.Parse("#263C48"), Palette.Text.Color);
    }

    [AvaloniaFact]
    public async Task Open_WithNoSavedTheme_ResetsTheColorsAnEarlierWindowChanged()
    {
        // Arrange
        Palette.Apply(new Dictionary<string, Color> { ["Text"] = Color.Parse("#E0E0E0") });
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();

        // Act
        await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Assert
        Assert.Equal(Color.Parse("#263C48"), Palette.Text.Color);
    }

    [AvaloniaFact]
    public async Task ApplyThemeChange_ToAFileTheme_ChangesTheColorsAtOnceAndSavesTheChoice()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Act
        window.ApplyThemeChange("Dusk");

        // Assert
        Assert.Equal("Dusk", window.ThemeName);
        Assert.Equal(Color.Parse("#102030"), ((ISolidColorBrush)window.Background!).Color);
        Assert.Equal("Dusk", workspace.ReadSavedTheme());
    }

    [AvaloniaFact]
    public async Task ApplyThemeChange_BackToTheBuiltInTheme_RestoresTheColorsAndSavesNoChoice()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        workspace.WriteSettings(theme: "Dusk");
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Act
        window.ApplyThemeChange(ColorTheme.BuiltInName);

        // Assert
        Assert.Equal(Color.Parse("#F6F8FA"), ((ISolidColorBrush)window.Background!).Color);
        Assert.Null(workspace.ReadSavedTheme());
    }

    [AvaloniaFact]
    public async Task Themes_ListTheBuiltInThemeFirstThenTheFiles()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        WriteTheme(workspace, "broken.json", "{ nope");
        workspace.WriteSettings();

        // Act
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);

        // Assert
        Assert.Equal([ColorTheme.BuiltInName, "Dusk"], window.Themes.Select(theme => theme.Name));
    }

    [AvaloniaFact]
    public async Task SeparateDataFolder_OffersOnlyTheBuiltInTheme()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        WriteTheme(workspace, "dusk.json", Dusk);

        // Act
        var window = await workspace.OpenWindowAsync();

        // Assert
        Assert.Equal([ColorTheme.BuiltInName], window.Themes.Select(theme => theme.Name));
    }

    [AvaloniaFact]
    public async Task SettingsPicker_ListsTheThemesAndChoosingOneShowsAndSavesIt()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        workspace.WriteSettings();
        var (window, settings) = await OpenSettingsAsync(workspace);
        var picker = settings.Find<ComboBox>("ThemePicker");
        var offered = picker.ItemsSource!.Cast<ColorTheme>().Select(theme => theme.Name).ToArray();
        var selectedAtFirst = ((ColorTheme)picker.SelectedItem!).Name;

        // Act
        picker.SelectedItem = picker.ItemsSource!.Cast<ColorTheme>().Single(theme => theme.Name == "Dusk");

        // Assert
        Assert.Equal([ColorTheme.BuiltInName, "Dusk"], offered);
        Assert.Equal(ColorTheme.BuiltInName, selectedAtFirst);
        Assert.Equal("Dusk", window.ThemeName);
        Assert.Equal("Dusk", workspace.ReadSavedTheme());
        Assert.Equal(Color.Parse("#E0E0E0"), Palette.Text.Color);
    }

    [AvaloniaFact]
    public async Task SettingsPicker_WithASavedTheme_StartsOnIt()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        WriteTheme(workspace, "dusk.json", Dusk);
        workspace.WriteSettings(theme: "Dusk");

        // Act
        var (_, settings) = await OpenSettingsAsync(workspace);

        // Assert
        Assert.Equal("Dusk", ((ColorTheme)settings.Find<ComboBox>("ThemePicker").SelectedItem!).Name);
    }

    [AvaloniaFact]
    public async Task SettingsPicker_LabelFollowsTheLanguage()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(language: "de");

        // Act
        var (_, settings) = await OpenSettingsAsync(workspace);

        // Assert
        Assert.Equal("Design", settings.Find<TextBlock>("ThemeLabel").Text);
    }
}
