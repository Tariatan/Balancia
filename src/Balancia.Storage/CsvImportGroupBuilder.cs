using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balancia.Storage;

internal static class CsvImportGroupBuilder
{
    public static List<CsvImportGroup> Build(List<CsvImportRow> rows, List<ImportIssue> issues)
    {
        var groups = new List<CsvImportGroup>();
        foreach (var grouping in rows.GroupBy(r => r.Id, StringComparer.Ordinal))
        {
            var first = grouping.First();
            if (grouping.Count() != 1)
            {
                issues.Add(new ImportIssue(first.Line, "Each Balancia export ID must occur exactly once."));
                continue;
            }

            string[][] canonical = [first.Fields];
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
            CsvImportRow[] movements = first.Type == "Transfer"
                ? [first with { Amount = checked(-first.Amount) }, first with { Account = first.DestinationAccount }]
                : [first];
            groups.Add(new CsvImportGroup(grouping.Key, first.Type, movements, fingerprint));
        }

        foreach (var account in groups.SelectMany(group => group.Rows).GroupBy(row => row.Account, StringComparer.OrdinalIgnoreCase))
        {
            var openings = account.Where(row => row.Type == "OpeningBalance").ToArray();
            if (openings.Length != 1)
            {
                issues.Add(new ImportIssue(account.First().Line, "Each account requires exactly one OpeningBalance row."));
            }
            else if (account.Any(row => row.Date < openings[0].Date))
            {
                issues.Add(new ImportIssue(openings[0].Line, "Opening balance must be on or before the account's first transaction."));
            }
        }
        return groups;
    }
}
