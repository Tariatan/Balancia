using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    internal async Task ImportCsv()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Get("Choose transactions CSV"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (path is null)
        {
            SetStatus("Choose a local CSV file.");
            return;
        }
        await Run(async () =>
        {
            var preview = await Task.Run(() => store.PreviewCsvImport(path));
            await CsvImportPreviewWindow.Show(this, preview);
        });
    }

    internal async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Get("Export CSV"),
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
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
        Serilog.Log.Information("Manual snapshot export requested");
        if (!await EnsureEncryption(dialogOwner ?? this))
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Get("Export Balancia snapshot"),
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.balancia",
            FileTypeChoices = [new FilePickerFileType(Get("Balancia snapshot")) { Patterns = ["*.balancia"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            Serilog.Log.Information("Manual snapshot export canceled; no local destination selected");
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
        Serilog.Log.Information("Manual snapshot restore requested");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Get("Choose Balancia snapshot"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Get("Balancia snapshot")) { Patterns = ["*.balancia"] }]
        });
        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (path is null)
        {
            Serilog.Log.Information("Manual snapshot restore canceled; no local source selected");
            return;
        }

        await Run(async () =>
        {
            var info = await Task.Run(() => SnapshotEncryption.ReadInfo(path));
            Serilog.Log.Information("Restore snapshot format detected, Encrypted: '{Encrypted}'", info is not null);
            SnapshotKey? temporaryKey = null;
            try
            {
                var key = snapshotKey;
                if (info is not null && (key is null || !key.Matches(info)))
                {
                    Serilog.Log.Information("Snapshot restore requires passphrase entry");
                    var passphrase = await SnapshotPassphraseWindow.Ask(dialogOwner ?? this, setup: false);
                    if (passphrase is null)
                    {
                        Serilog.Log.Information("Snapshot restore unlock canceled; current ledger unchanged");
                        return;
                    }

                    temporaryKey = await Task.Run(() => SnapshotKey.Derive(passphrase, info));
                    key = temporaryKey;
                }
                else if (info is not null)
                {
                    Serilog.Log.Information("Snapshot restore using remembered key");
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
}
