namespace Balancia.Storage;

internal static class CsvImportSummaryBuilder
{
    public static ImportSummary Build(List<CsvImportRow> rows, List<CsvImportGroup> groups, int sourceRows)
    {
        var totals = groups.Where(group => group.Kind != "Reminder").SelectMany(group => group.Rows)
            .GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ImportAccountTotal(g.Key, checked((long)g.Sum(r => (decimal)r.Amount))))
            .OrderBy(x => x.Account)
            .ToArray();
        var categories = rows
            .Where(r => r.Category.Length > 0)
            .Select(r => r.Category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToArray();
        return new ImportSummary(
            sourceRows,
            groups.Count(g => g.Kind == "Expense"),
            groups.Count(g => g.Kind == "Income"),
            groups.Count(g => g.Kind == "Transfer"),
            groups.Count(g => g.Kind == "OpeningBalance"),
            totals,
            categories, groups.Count(group => group.Kind == "Reminder"));
    }
}
