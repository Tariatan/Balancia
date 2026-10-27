using System.Globalization;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class CsvImportPreviewWindow : Window
{
    private Func<CsvImportPreview, ImportResult> apply = null!;
    private Func<ImportResult, Task> onApplied = null!;
    private CsvImportPreview preview = null!;

    public CsvImportPreviewWindow()
    {
        InitializeComponent();
    }

    public static Task Show(
        Window owner,
        CsvImportPreview preview,
        Func<CsvImportPreview, ImportResult> apply,
        Func<ImportResult, Task> onApplied)
    {
        var window = new CsvImportPreviewWindow { preview = preview, apply = apply, onApplied = onApplied };
        window.Initialize();
        return window.ShowDialog(owner);
    }

    private void Initialize()
    {
        Title = Get("CSV import preview");
        MessageText.Text = BuildMessage();
        ApplyButton.Content = Get("Apply import");
        ApplyButton.IsEnabled = preview.CanApply;
        CancelButton.Content = Get("Cancel");
    }

    private string BuildMessage()
    {
        var summary = preview.Summary;
        var dates = string.Join(", ", preview.ResolvedDates.Select(d =>
            Format("Resolved date line {0}: {1}", d.Line, d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        var accounts = string.Join("\n", summary.AccountTotals.Select(a => $"{a.Account}: {a.Centimes / 100m:N2}"));
        var outcome = preview.Issues.Count == 0
            ? Get("All rows are valid. Apply this batch?")
            : Get("Errors (correct source file, then preview again):") + "\n" +
              string.Join("\n", preview.Issues.Take(30).Select(i => Format("Line {0}: {1}", i.Line, Get(i.Message))));

        return string.Concat(
            Format("{0} rows: {1} expenses, {2} income, {3} transfers, {4} openings.", summary.Rows, summary.Expenses, summary.Incomes, summary.Transfers, summary.Openings),
            "\n\n",
            Format("Reminders: {0}", summary.Reminders),
            "\n\n",
            Get("Dates (first 10 rows): "), dates,
            "\n\n", Get("Accounts and source totals:"), "\n", accounts,
            "\n\n", Get("Categories: "), string.Join(", ", summary.Categories),
            "\n\n", outcome);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ErrorText.Text = null;
        try
        {
            Serilog.Log.Information("Applying CSV import");
            var result = await Task.Run(() => apply(preview));
            Serilog.Log.Information("CSV import completed, Added: '{Added}', Unchanged: '{Unchanged}'", result.Added, result.Unchanged);
            Close();
            await onApplied(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CSV import failed");
            ErrorText.Text = FriendlyErrors.For(ex);
            ApplyButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }
}
