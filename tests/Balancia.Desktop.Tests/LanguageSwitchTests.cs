using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class LanguageSwitchTests
{
    [AvaloniaFact]
    public async Task ApplyLanguageChange_ToGerman_TranslatesTheOverviewAndSavesTheChoice()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: false);
        await DesktopWorkspace.OpenAsync(window);
        var englishAllButton = window.Find<Button>("FilterButtonAll").Content;

        // Act
        await window.ApplyLanguageChange("de");

        // Assert
        Assert.StartsWith("Buchungen", window.TransactionsHeading());
        Assert.NotEqual(englishAllButton, window.Find<Button>("FilterButtonAll").Content);
        Assert.Equal("de", window.LanguageCode);
        using var saved = JsonDocument.Parse(File.ReadAllText(workspace.SettingsFile));
        Assert.Equal("de", saved.RootElement.GetProperty("Language").GetString());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Open_WithSavedGermanLanguage_StartsInGerman()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(language: "de");
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: false);

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Equal("de", window.LanguageCode);
        Assert.StartsWith("Buchungen", window.TransactionsHeading());
        window.Close();
    }
}
