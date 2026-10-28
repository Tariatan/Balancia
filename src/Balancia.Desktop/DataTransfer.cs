using Avalonia.Platform.Storage;
using System.Security.Cryptography;
using Avalonia.Controls;
using Balancia.Storage;
using Serilog;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static FilePickerFileType CsvFileType => new("CSV") { Patterns = ["*.csv"] };

    private static FilePickerFileType SnapshotFileType => new(Get("Balancia snapshot")) { Patterns = ["*.balancia"] };

    // The file and folder pickers are replaceable so tests can answer them without a native dialog.
    internal Func<string, FilePickerFileType, Task<string?>> OpenFilePicker { get; set; }

    internal Func<string, string, FilePickerFileType, Task<string?>> SaveFilePicker { get; set; }

    internal Func<string, Task<string?>> FolderPicker { get; set; }

    internal async Task ImportCsv()
    {
        var path = await OpenFilePicker(Get("Choose transactions CSV"), CsvFileType);
        if (path is null)
        {
            return;
        }

        await operations.RunAsync(async () =>
        {
            var preview = await Task.Run(() => store.PreviewCsvImport(path));
            await CsvImportPreviewWindow.Show(this, preview, store.ApplyCsvImport, async result =>
            {
                await Refresh();
                await ForgetMissingDefaultAccount();
                SetStatus(Format("Replaced the ledger with {0} imported entries.", result.Imported));
            });
        });
    }

    /// <summary>An import gives the accounts new IDs, so a default account that is no longer there is cleared and the owner picks one again.</summary>
    private async Task ForgetMissingDefaultAccount()
    {
        if (settings.DefaultAccountId is not { } id || OverviewPane.Data!.Snapshot.Accounts.Any(account => account.Id == id))
        {
            return;
        }

        var updated = settings with { DefaultAccountId = null };
        await Task.Run(() => PersistSettings(updated));
        settings = updated;
    }

    internal async Task ExportCsv()
    {
        var path = await SaveFilePicker(Get("Export CSV"), $"balancia-{DateTime.Today:yyyyMMdd}.csv", CsvFileType);
        if (path is null)
        {
            return;
        }

        await operations.RunAsync(async () =>
        {
            var count = await Task.Run(() => store.ExportCsv(path));
            SetStatus(Format("Exported {0} rows to CSV.", count));
        });
    }

    internal async Task ExportSnapshot(Window? dialogOwner = null)
    {
        Log.Information("Manual snapshot export requested");
        if (!await EnsureEncryption(dialogOwner ?? this))
        {
            return;
        }

        var path = await SaveFilePicker(Get("Export Balancia snapshot"), $"balancia-{DateTime.Today:yyyyMMdd}.balancia", SnapshotFileType);
        if (path is null)
        {
            Log.Information("Manual snapshot export canceled; no local destination selected");
            return;
        }

        await operations.RunAsync(async () =>
        {
            var manifest = await Task.Run(() => store.ExportSnapshot(path, keys.Key!));
            SetStatus(Format("Snapshot exported · revision {0}", manifest.Revision));
        }, errorOwner: dialogOwner, operationName: "Snapshot export");
    }

    internal async Task RestoreSnapshot(Window? dialogOwner = null)
    {
        Log.Information("Manual snapshot restore requested");
        var path = await OpenFilePicker(Get("Choose Balancia snapshot"), SnapshotFileType);
        if (path is null)
        {
            Log.Information("Manual snapshot restore canceled; no local source selected");
            return;
        }

        await operations.RunAsync(async () =>
        {
            var unlocked = await SnapshotUnlock.RunAsync(path, new RestoreUnlockHost(keys, dialogOwner ?? this),
                key => Task.Run(() => store.RestoreSnapshot(path, key)));
            if (unlocked.Canceled)
            {
                Log.Information("Snapshot restore unlock canceled; current ledger unchanged");
                return;
            }

            var result = unlocked.Value!;
            SetStatus(Format("Snapshot restored · revision {0} · backup {1}", result.Manifest.Revision, Path.GetFileName(result.BackupPath)));
            await Refresh();
        }, errorOwner: dialogOwner, operationName: "Snapshot restore");
    }

    /// <summary>
    /// Unlocks a snapshot for a restore: offers the key Windows remembers and asks for the passphrase otherwise, again after a wrong one.
    /// A typed passphrase is not remembered, because the remembered key is the one that encrypts exports and backups.
    /// </summary>
    private sealed class RestoreUnlockHost(SnapshotKeyService keys, Window owner) : ISnapshotUnlockHost
    {
        public SnapshotKey? LoadRememberedKey()
        {
            if (keys.Key is not { } key)
            {
                return null;
            }

            // The unlock disposes the key it is given, and the remembered one stays in use.
            var secret = key.ExportSecret();
            try
            {
                return SnapshotKey.ImportSecret(secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        public Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error) =>
            SnapshotPassphraseWindow.Ask(owner, setup: false, error);
    }

    private async Task<string?> PickOpenFile(string title, FilePickerFileType fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [fileType]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickSaveFile(string title, string suggestedFileName, FilePickerFileType fileType)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [fileType]
        });

        return file?.TryGetLocalPath();
    }
}
