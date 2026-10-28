using Balancia.Core;

namespace Balancia.Storage;

internal static class CsvImportSummaryBuilder
{
    public static ImportSummary Build(List<CsvImportRow> rows, List<CsvImportGroup> groups, int sourceRows)
    {
        var kindCounts = new int[Enum.GetValues<CsvRowType>().Length];
        var accountTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            kindCounts[(int)group.Kind]++;

            if (group.Kind == CsvRowType.Reminder)
            {
                continue;
            }

            foreach (var row in group.Rows)
            {
                accountTotals[row.Account] = accountTotals.GetValueOrDefault(row.Account) + row.Amount;
            }
        }

        var totals = accountTotals
            .Select(pair => new ImportAccountTotal(pair.Key, (long)pair.Value))
            .OrderBy(total => total.Account)
            .ToArray();
        var categories = rows
            .Where(row => row.Category.Length > 0)
            .Select(row => CategoryRules.Prettify(row.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category)
            .ToArray();

        return new ImportSummary(
            sourceRows,
            kindCounts[(int)CsvRowType.Expense],
            kindCounts[(int)CsvRowType.Income],
            kindCounts[(int)CsvRowType.Transfer],
            kindCounts[(int)CsvRowType.OpeningBalance],
            totals,
            categories,
            kindCounts[(int)CsvRowType.Reminder]);
    }
}
