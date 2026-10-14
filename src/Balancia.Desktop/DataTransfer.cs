using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Balancia.Storage;
using Serilog;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static FilePickerFileType CsvFileType => new("CSV") { Patterns = ["*.csv"] };

    private static FilePickerFileType SnapshotFileType => new(Get("Balancia snapshot")) { Patterns = ["*.balancia"] };

    internal async Task ImportCsv()
    {
        var file = await PickOpenFile(Get("Choose transactions CSV"), CsvFileType);
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            SetStatus("Choose a local CSV file.");
            return;
        }
        await Run(async () =>
        {
            var preview = await Task.Run(() => store.PreviewCsvImport(path));
            await CsvImportPreviewWindow.Show(this, preview, store.ApplyCsvImport, async result =>
            {
                await Refresh();
                SetStatus(Format("Imported {0} entries; {1} unchanged.", result.Added, result.Unchanged));
            });
        });
    }

    internal async Task ExportCsv()
    {
        var file = await PickSaveFile(Get("Export CSV"), $"balancia-{DateTime.Today:yyyyMMdd}.csv", CsvFileType);
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
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

        var file = await PickSaveFile(Get("Export Balancia snapshot"), $"balancia-{DateTime.Today:yyyyMMdd}.balancia", SnapshotFileType);
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            Log.Information("Manual snapshot export canceled; no local destination selected");
            return;
        }

        await Run(async () =>
        {
            var manifest = await Task.Run(() => store.ExportSnapshot(path, snapshotKey!));
            SetStatus(Format("Snapshot exported · revision {0}", manifest.Revision));
        }, errorOwner: dialogOwner, operationName: "Snapshot export");
    }

    internal async Task RestoreSnapshot(Window? dialogOwner = null)
    {
        Log.Information("Manual snapshot restore requested");
        var file = await PickOpenFile(Get("Choose Balancia snapshot"), SnapshotFileType);
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            Log.Information("Manual snapshot restore canceled; no local source selected");
            return;
        }

        await Run(async () =>
        {
            var info = await Task.Run(() => SnapshotEncryption.ReadInfo(path));
            Log.Information("Restore snapshot format detected, Encrypted: '{Encrypted}'", info is not null);
            SnapshotKey? temporaryKey = null;
            try
            {
                var key = snapshotKey;
                if (info is not null && (key is null || !key.Matches(info)))
                {
                    Log.Information("Snapshot restore requires passphrase entry");
                    var passphrase = await SnapshotPassphraseWindow.Ask(dialogOwner ?? this, setup: false);
                    if (passphrase is null)
                    {
                        Log.Information("Snapshot restore unlock canceled; current ledger unchanged");
                        return;
                    }

                    temporaryKey = await Task.Run(() => SnapshotKey.Derive(passphrase, info));
                    key = temporaryKey;
                }
                else if (info is not null)
                {
                    Log.Information("Snapshot restore using remembered key");
                }

                var result = await Task.Run(() => store.RestoreSnapshot(path, key));
                SetStatus(Format("Snapshot restored · revision {0} · backup {1}", result.Manifest.Revision, Path.GetFileName(result.BackupPath)));
                await Refresh();
            }
            finally
            {
                temporaryKey?.Dispose();
            }
        }, errorOwner: dialogOwner, operationName: "Snapshot restore");
    }

    private async Task<IStorageFile?> PickOpenFile(string title, FilePickerFileType fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [fileType]
        });

        return files.FirstOrDefault();
    }

    private Task<IStorageFile?> PickSaveFile(string title, string suggestedFileName, FilePickerFileType fileType) =>
        StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [fileType]
        });
}
