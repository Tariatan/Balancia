using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private async Task ImportCsv()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose transactions CSV",
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
            Status.Text = "Choose a local CSV file.";
            return;
        }
        await Run(async () =>
        {
            var preview = await Task.Run(() => store.PreviewCsvImport(path));
            var summary = preview.Summary;
            var message = $"{summary.Rows} rows: {summary.Expenses} expenses, {summary.Incomes} income, {summary.Transfers} paired transfers, {summary.Openings} openings.\n\n" +
                "Resolved dates (first 10 rows, 2000–2099): " +
                string.Join(", ", preview.ResolvedDates.Select(d => $"line {d.Line}: {d.Date:yyyy-MM-dd}")) +
                "\n\nAccounts and source totals:\n" +
                string.Join("\n", summary.AccountTotals.Select(a => $"{a.Account}: {a.Centimes / 100m:N2}")) +
                "\n\nCategories: " + string.Join(", ", summary.Categories) +
                (preview.Issues.Count == 0 ? "\n\nAll rows are valid. Apply this batch?" :
                    "\n\nErrors (correct source file, then preview again):\n" +
                    string.Join("\n", preview.Issues.Take(30).Select(i => $"Line {i.Line}: {i.Message}")));
            var dialog = new Window
            {
                Title = "CSV import preview",
                Icon = Icon,
                ShowInTaskbar = false,
                Width = 700,
                Height = 650,
                MinWidth = 500,
                MinHeight = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var body = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(20)
            };
            body.Children.Add(Text(message));
            var apply = new Button
            {
                Content = "Apply import",
                IsEnabled = preview.CanApply
            };
            var cancel = new Button
            {
                Content = "Cancel",
                IsCancel = true
            };
            var error = Text("");
            error.Foreground = Brushes.DarkRed;
            body.Children.Add(error);
            body.Children.Add(Row(apply, cancel));
            dialog.Content = new ScrollViewer { Content = body };
            cancel.Click += (_, _) => dialog.Close();
            apply.Click += async (_, _) =>
            {
                apply.IsEnabled = false;
                try
                {
                    var result = await Task.Run(() => store.ApplyCsvImport(preview));
                    dialog.Close();
                    await Refresh();
                    Status.Text = $"Imported {result.Added} entries; {result.Unchanged} unchanged.";
                }
                catch (Exception ex)
                {
                    error.Text = FriendlyError(ex);
                    apply.IsEnabled = true;
                }
            };
            await dialog.ShowDialog(this);
        });
    }

    private async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
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
            Status.Text = $"Exported {count} rows to CSV.";
        });
    }

    private async Task ExportSnapshot()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Balancia snapshot",
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.balancia",
            FileTypeChoices = [new FilePickerFileType("Balancia snapshot") { Patterns = ["*.balancia"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var manifest = await Task.Run(() => store.ExportSnapshot(path));
            Status.Text = $"Snapshot exported · revision {manifest.Revision}";
        });
    }

    private async Task RestoreSnapshot()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Balancia snapshot",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Balancia snapshot") { Patterns = ["*.balancia"] }]
        });
        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var result = await Task.Run(() => store.RestoreSnapshot(path));
            Status.Text = $"Snapshot restored · revision {result.Manifest.Revision} · backup {Path.GetFileName(result.BackupPath)}";
            await Refresh();
        });
    }
}
