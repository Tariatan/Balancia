using Avalonia.Platform.Storage;

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

    internal async Task ExportSnapshot()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Get("Export Balancia snapshot"),
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.balancia",
            FileTypeChoices = [new FilePickerFileType(Get("Balancia snapshot")) { Patterns = ["*.balancia"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var manifest = await Task.Run(() => store.ExportSnapshot(path));
            SetStatus(Format("Snapshot exported · revision {0}", manifest.Revision));
        });
    }

    internal async Task RestoreSnapshot()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Get("Choose Balancia snapshot"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Get("Balancia snapshot")) { Patterns = ["*.balancia"] }]
        });
        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var result = await Task.Run(() => store.RestoreSnapshot(path));
            SetStatus(Format("Snapshot restored · revision {0} · backup {1}", result.Manifest.Revision, Path.GetFileName(result.BackupPath)));
            await Refresh();
        });
    }
}
