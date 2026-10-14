using Avalonia.Platform.Storage;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Task OpenSettingsDialog() => SettingsWindow.ShowFor(this);

    private async Task<string?> PickFolder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Get(title),
            AllowMultiple = false
        });
        var directory = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return directory is null ? null : Path.GetFullPath(directory);
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
            await Task.Run(() => SaveApplicationSettings(dbPath));
            store = replacement;
            DatabasePath = dbPath;
            Serilog.Log.Information("Database location changed, DatabasePath: '{DatabasePath}'", dbPath);
            await Refresh();
        });
    }

    internal Task ChooseBackupLocation(Avalonia.Controls.Window? dialogOwner = null) => ChooseEncryptedLocation(
        "Choose backup folder",
        dialogOwner,
        selected => SaveApplicationSettings(DatabasePath, selected),
        selected =>
        {
            BackupPath = selected;
            Serilog.Log.Information("Backup location changed, BackupPath: '{BackupPath}'", selected);
        });

    internal Task ChooseSnapshotLocation(Avalonia.Controls.Window? dialogOwner = null) => ChooseEncryptedLocation(
        "Choose snapshot folder",
        dialogOwner,
        selected => SaveApplicationSettings(DatabasePath, BackupPath, selected),
        selected =>
        {
            SnapshotPath = selected;
            Serilog.Log.Information("Snapshot location changed, SnapshotPath: '{SnapshotPath}'", selected);
        });

    private async Task ChooseEncryptedLocation(string title, Avalonia.Controls.Window? dialogOwner, Action<string> save, Action<string> apply)
    {
        var selected = await PickFolder(title);
        if (selected is null || !await EnsureEncryption(dialogOwner ?? this))
        {
            return;
        }

        await Run(async () =>
        {
            await Task.Run(() => save(selected));
            apply(selected);
            await Refresh();
        });
    }
}
