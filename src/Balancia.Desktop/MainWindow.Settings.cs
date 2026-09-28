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
        if (string.Equals(dbPath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("This database folder is already active.");
            return;
        }

        await Run(async () =>
        {
            var replacement = new LedgerStore(dbPath);
            await Task.Run(replacement.Initialize);
            await Task.Run(() => SaveApplicationSettings(dbPath));
            store = replacement;
            databasePath = dbPath;
            windowSettingsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "window.json");
            await Refresh();
        });
    }

    internal async Task ChooseBackupLocation()
    {
        var selected = await PickFolder("Choose backup folder");
        if (selected is null)
        {
            return;
        }

        await Run(async () =>
        {
            await Task.Run(() => SaveApplicationSettings(databasePath, selected));
            backupPath = selected;
            await Refresh();
        });
    }

    internal async Task ChooseSnapshotLocation()
    {
        var selected = await PickFolder("Choose snapshot folder");
        if (selected is null)
        {
            return;
        }

        await Run(async () =>
        {
            await Task.Run(() => SaveApplicationSettings(databasePath, backupPath, selected));
            snapshotPath = selected;
            await Refresh();
        });
    }
}
