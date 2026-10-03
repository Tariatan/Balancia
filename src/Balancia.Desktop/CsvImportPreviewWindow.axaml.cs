using Avalonia.Controls;
using Avalonia.Interactivity;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class CsvImportPreviewWindow : Window
{
    private MainWindow owner = null!;
    private CsvImportPreview preview = null!;

    public CsvImportPreviewWindow()
    {
        InitializeComponent();
    }

    public static Task Show(MainWindow owner, CsvImportPreview preview)
    {
        var window = new CsvImportPreviewWindow { owner = owner, preview = preview };
        window.Initialize();
        return window.ShowDialog(owner);
    }

    private void Initialize()
    {
        Title = Get("CSV import preview");
        messageText.Text = BuildMessage();
        applyButton.Content = Get("Apply import");
        applyButton.IsEnabled = preview.CanApply;
        cancelButton.Content = Get("Cancel");
        applyButton.Click += OnApply;
        cancelButton.Click += (_, _) => Close();
    }

    private string BuildMessage()
    {
        var summary = preview.Summary;
        return Format("{0} rows: {1} expenses, {2} income, {3} transfers, {4} openings.\n\n",
                summary.Rows, summary.Expenses, summary.Incomes, summary.Transfers, summary.Openings) +
            Format("Reminders: {0}", summary.Reminders) + "\n\n" +
            Get("Dates (first 10 rows): ") +
            string.Join(", ", preview.ResolvedDates.Select(d => Format("Resolved date line {0}: {1}", d.Line, d.Date.ToString("yyyy-MM-dd")))) +
            Get("\n\nAccounts and source totals:\n") +
            string.Join("\n", summary.AccountTotals.Select(a => $"{a.Account}: {a.Centimes / 100m:N2}")) +
            Get("\n\nCategories: ") + string.Join(", ", summary.Categories) +
            (preview.Issues.Count == 0 ? Get("\n\nAll rows are valid. Apply this batch?") :
                Get("\n\nErrors (correct source file, then preview again):\n") +
                string.Join("\n", preview.Issues.Take(30).Select(i => Format("Line {0}: {1}", i.Line, Get(i.Message)))));
    }

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        applyButton.IsEnabled = false;
        try
        {
            Serilog.Log.Information("Applying CSV import");
            var result = await Task.Run(() => owner.store.ApplyCsvImport(preview));
            Serilog.Log.Information("CSV import completed, Added: '{Added}', Unchanged: '{Unchanged}'", result.Added, result.Unchanged);
            Close();
            await owner.Refresh();
            owner.SetStatus(Format("Imported {0} entries; {1} unchanged.", result.Added, result.Unchanged));
        }
        catch (Exception ex)
        {
            MainWindow.LogWorkflowFailure(ex, "CSV import");
            errorText.Text = MainWindow.FriendlyError(ex);
            applyButton.IsEnabled = true;
        }
    }
}
