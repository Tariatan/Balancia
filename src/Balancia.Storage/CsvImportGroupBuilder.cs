using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balancia.Storage;

internal static class CsvImportGroupBuilder
{
    public static List<CsvImportGroup> Build(IReadOnlyList<CsvImportRow> rows, List<ImportIssue> issues)
    {
        var groups = BuildGroups(rows, issues);
        ReportDuplicateReminders(rows, issues);
        ReportOpeningBalanceIssues(groups, issues);
        return groups;
    }

    private static List<CsvImportGroup> BuildGroups(IReadOnlyList<CsvImportRow> rows, List<ImportIssue> issues)
    {
        var groups = new List<CsvImportGroup>();
        foreach (var grouping in rows.GroupBy(row => row.Id, StringComparer.Ordinal))
        {
            var first = grouping.First();
            if (grouping.Skip(1).Any())
            {
                issues.Add(new ImportIssue(first.Line, "Each Balancia export ID must occur exactly once."));
                continue;
            }

            CsvImportRow[] movements = first.Type == CsvRowType.Transfer
                ? [first with { Amount = checked(-first.Amount) }, first with { Account = first.DestinationAccount }]
                : [first];
            groups.Add(new CsvImportGroup(grouping.Key, first.Type, movements, Fingerprint(first)));
        }

        return groups;
    }

    private static string Fingerprint(CsvImportRow row)
    {
        string[][] canonical = [row.Fields];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    private static void ReportDuplicateReminders(IReadOnlyList<CsvImportRow> rows, List<ImportIssue> issues)
    {
        var duplicates = rows
            .Where(row => row.Type == CsvRowType.Reminder && !JsonSerializer.Deserialize<CsvReminderOptions>(row.Memo)!.Archived)
            .GroupBy(row => row.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any());

        issues.AddRange(duplicates.Select(duplicate
            => new ImportIssue(duplicate.First().Line, "Duplicate reminder description.")));
    }

    private static void ReportOpeningBalanceIssues(List<CsvImportGroup> groups, List<ImportIssue> issues)
    {
        var accounts = groups
            .Where(group => group.Kind != CsvRowType.Reminder)
            .SelectMany(group => group.Rows)
            .GroupBy(row => row.Account, StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts)
        {
            var openings = account.Where(row => row.Type == CsvRowType.OpeningBalance).ToArray();
            if (openings.Length != 1)
            {
                issues.Add(new ImportIssue(account.First().Line, "Each account requires exactly one OpeningBalance row."));
            }
            else if (account.Any(row => row.Date < openings[0].Date))
            {
                issues.Add(new ImportIssue(openings[0].Line, "Opening balance must be on or before the account's first transaction."));
            }
        }
    }
}
