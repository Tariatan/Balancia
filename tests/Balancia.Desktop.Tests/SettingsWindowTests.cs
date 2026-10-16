using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>The Settings dialog driven through its buttons, with folder and file pickers answered by the test.</summary>
public sealed class SettingsWindowTests
{
    private static async Task<(MainWindow Window, SettingsWindow Settings)> OpenSettingsAsync(DesktopWorkspace workspace, bool withKey = true)
    {
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = workspace.CreateWindowWithKeyStore(new FakeKeyStore());
        if (withKey)
        {
            window.UseSnapshotKey(DesktopWorkspace.CreateKey());
        }

        await DesktopWorkspace.OpenAsync(window);
        window.Click("SettingsButton");
        var settings = await window.WaitForDialogAsync<SettingsWindow>();
        return (window, settings);
    }

    private static Task SettledAsync(SettingsWindow settings) =>
        DesktopWorkspace.WaitUntilAsync(() => settings.Find<Grid>("ContentRoot").IsEnabled, "the settings action to finish");

    private static string SavedSetting(DesktopWorkspace workspace, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(workspace.SettingsFile));
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    }

    [AvaloniaFact]
    public async Task Open_ShowsTheCurrentLocationsVersionAndEncryptionState()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();

        // Act
        var (window, settings) = await OpenSettingsAsync(workspace);

        // Assert
        Assert.Equal("Settings", settings.Title);
        Assert.Equal(window.DatabasePath, settings.Text("DatabaseText"));
        Assert.Equal("Not configured", settings.Text("BackupText"));
        Assert.Equal("Not configured", settings.Text("SnapshotText"));
        Assert.StartsWith("Encrypted snapshots and backups.", settings.Text("EncryptionStatus"));
        Assert.StartsWith("Balancia ", settings.Text("VersionText"));
        Assert.Equal("Change passphrase", settings.Find<Button>("EncryptionButton").Content);
        settings.ClickDialogButton("Close");
        await window.WaitUntilClosedAsync();
    }

    [AvaloniaFact]
    public async Task BackupAndSnapshotFolders_Chosen_AreShownAndSaved()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        window.FolderPicker = title => Task.FromResult<string?>(title.Contains("backup") ? workspace.BackupDirectory : workspace.SnapshotDirectory);

        // Act
        settings.ClickDialogButton("Backup folder");
        await SettledAsync(settings);
        settings.ClickDialogButton("Snapshot folder");
        await SettledAsync(settings);

        // Assert
        Assert.Equal(workspace.BackupDirectory, settings.Text("BackupText"));
        Assert.Equal(workspace.SnapshotDirectory, settings.Text("SnapshotText"));
        Assert.Equal(workspace.BackupDirectory, SavedSetting(workspace, "BackupPath"));
        Assert.Equal(workspace.SnapshotDirectory, SavedSetting(workspace, "SnapshotPath"));
        Assert.Equal(workspace.DatabasePath, SavedSetting(workspace, "DatabasePath"));
        Assert.Equal("en", SavedSetting(workspace, "Language"));
    }

    [AvaloniaFact]
    public async Task BackupFolder_PickerCanceled_ChangesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        window.FolderPicker = _ => Task.FromResult<string?>(null);

        // Act
        settings.ClickDialogButton("Backup folder");
        await SettledAsync(settings);

        // Assert
        Assert.Equal("Not configured", settings.Text("BackupText"));
        Assert.Equal("", SavedSetting(workspace, "BackupPath"));
    }

    [AvaloniaFact]
    public async Task BackupFolder_WithoutKeyAndSetupCanceled_IsNotSaved()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace, withKey: false);
        window.FolderPicker = _ => Task.FromResult<string?>(workspace.BackupDirectory);

        // Act
        settings.ClickDialogButton("Backup folder");
        var setup = await settings.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.ClickDialogButton("Cancel");
        await SettledAsync(settings);

        // Assert
        Assert.Equal("Not configured", settings.Text("BackupText"));
        Assert.Equal("", SavedSetting(workspace, "BackupPath"));
    }

    [AvaloniaFact]
    public async Task DatabaseFolder_ChosenElsewhere_SwitchesTheLedgerAndSavesThePath()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        var other = Directory.CreateDirectory(Path.Combine(workspace.Root, "other-data")).FullName;
        window.FolderPicker = _ => Task.FromResult<string?>(other);

        // Act
        settings.ClickDialogButton("Database folder");
        await SettledAsync(settings);

        // Assert
        var expected = Path.Combine(other, "balancia.db");
        Assert.Equal(expected, window.DatabasePath);
        Assert.Equal(expected, settings.Text("DatabaseText"));
        Assert.Equal(expected, SavedSetting(workspace, "DatabasePath"));
        Assert.True(File.Exists(expected));
    }

    [AvaloniaFact]
    public async Task DatabaseFolder_AlreadyActive_ReportsItWithoutChanges()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        window.FolderPicker = _ => Task.FromResult<string?>(workspace.DataDirectory);

        // Act
        settings.ClickDialogButton("Database folder");
        await SettledAsync(settings);

        // Assert
        Assert.Equal("This database folder is already active.", window.Text("OverviewStatus"));
        Assert.Equal(workspace.DatabasePath, window.DatabasePath);
    }

    [AvaloniaFact]
    public async Task Language_SelectedGerman_TranslatesTheDialogAndSavesTheChoice()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        var picker = settings.Find<ComboBox>("LanguagePicker");

        // Act
        picker.SelectedItem = MainWindow.Languages.Single(option => option.Code == "de");
        await DesktopWorkspace.WaitUntilAsync(() => settings.Title == "Einstellungen", "the dialog to switch language");

        // Assert
        Assert.Equal("de", window.LanguageCode);
        Assert.Equal("de", SavedSetting(workspace, "Language"));
        Assert.Equal("Backup-Ordner", settings.Find<Button>("BackupButton").Content);
    }

    [AvaloniaFact]
    public async Task Encryption_ChangePassphrase_ReplacesTheRememberedKey()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace, withKey: false);

        // Act
        settings.ClickDialogButton("Set passphrase");
        var setup = await settings.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.SubmitPassphrase("synthetic passphrase 123", "Save");
        await DesktopWorkspace.WaitUntilAsync(() => settings.Text("EncryptionStatus").StartsWith("Encrypted"), "encryption to be configured");
        await SettledAsync(settings);

        // Assert
        Assert.True(window.EncryptionConfigured);
        Assert.Equal("Change passphrase", settings.Find<Button>("EncryptionButton").Content);
    }

    [AvaloniaFact]
    public async Task DataTransferButtons_ExportCsvAndSnapshot_UseTheOwnerPickers()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        var csv = Path.Combine(workspace.Root, "settings-out.csv");
        var snapshot = Path.Combine(workspace.Root, "settings-out.balancia");
        window.SaveFilePicker = (_, name, _) => Task.FromResult<string?>(name.EndsWith(".csv") ? csv : snapshot);

        // Act
        settings.ClickDialogButton("Export CSV");
        await SettledAsync(settings);
        settings.ClickDialogButton("Export snapshot");
        await SettledAsync(settings);

        // Assert
        Assert.True(File.Exists(csv));
        Assert.NotNull(workspace.ValidateExport(snapshot));
    }

    [AvaloniaFact]
    public async Task Close_WhileAnActionRuns_StaysOpenUntilItFinishes()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, settings) = await OpenSettingsAsync(workspace);
        var release = new TaskCompletionSource<string?>();
        window.FolderPicker = _ => release.Task;
        settings.ClickDialogButton("Backup folder");

        // Act
        settings.Close();
        var stayedOpen = settings.IsVisible;
        release.SetResult(null);
        await SettledAsync(settings);
        settings.Close();

        // Assert
        Assert.True(stayedOpen);
        await window.WaitUntilClosedAsync();
    }
}
