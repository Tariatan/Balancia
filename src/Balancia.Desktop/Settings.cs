using Avalonia.Platform.Storage;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Task OpenSettingsDialog() => SettingsWindow.ShowFor(this);

    private async Task<string?> PickFolder(string title) =>
        await FolderPicker(title) is { } directory ? Path.GetFullPath(directory) : null;

    private async Task<string?> PickFolderFromDialog(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Get(title),
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    internal async Task ChangeDatabaseLocation()
    {
        var directory = await PickFolder("Choose Balancia database folder");
        if (directory is null)
        {
            return;
        }

        var dbPath = Path.Combine(directory, "balancia.db");
        if (string.Equals(dbPath, DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("This database folder is already active.");
            return;
        }

        await Run(async () =>
        {
            Serilog.Log.Information("Changing database location, DatabasePath: '{DatabasePath}'", dbPath);
            var replacement = new LedgerStore(dbPath);
            await Task.Run(replacement.Initialize);
            var updated = settings with { DatabasePath = dbPath };
            await Task.Run(() => PersistSettings(updated));
            settings = updated;
            store = replacement;
            closeExporter = new CloseTimeExporter(store, timeProvider);
            closeExporter.MarkLoaded();
            DatabasePath = dbPath;
            Serilog.Log.Information("Database location changed, DatabasePath: '{DatabasePath}'", dbPath);
            await Refresh();
        });
    }

    internal Task ChooseBackupLocation(Avalonia.Controls.Window? dialogOwner = null) => ChooseEncryptedLocation(
        "Choose backup folder",
        dialogOwner,
        (current, selected) => current with { BackupPath = selected },
        selected => Serilog.Log.Information("Backup location changed, BackupPath: '{BackupPath}'", selected));

    internal Task ChooseSnapshotLocation(Avalonia.Controls.Window? dialogOwner = null) => ChooseEncryptedLocation(
        "Choose snapshot folder",
        dialogOwner,
        (current, selected) => current with { SnapshotPath = selected },
        selected => Serilog.Log.Information("Snapshot location changed, SnapshotPath: '{SnapshotPath}'", selected));

    private async Task ChooseEncryptedLocation(string title, Avalonia.Controls.Window? dialogOwner,
        Func<ApplicationSettings, string, ApplicationSettings> change, Action<string> onChanged)
    {
        var selected = await PickFolder(title);
        if (selected is null || !await EnsureEncryption(dialogOwner ?? this))
        {
            return;
        }

        await Run(async () =>
        {
            var updated = change(settings, selected);
            await Task.Run(() => PersistSettings(updated));
            settings = updated;
            onChanged(selected);
            await Refresh();
        });
    }

    /// <summary>Writes the settings file with the current window geometry. A separate data folder never touches the user's settings.</summary>
    private void PersistSettings(ApplicationSettings updated)
    {
        if (!separateDataFolder)
        {
            settingsStore.Save(updated with { Window = windowSettings });
        }
    }
}
