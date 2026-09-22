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
            var members = grouping.ToArray();
            var first = members[0];
            var kind = first.Type;
            if (kind == "Transfer" && members.Length == 1 && first.Description == "Opening balance")
            {
                kind = "OpeningBalance";
            }

            if (kind == "Transfer" && (members.Length != 2 || members[0].Amount != -members[1].Amount ||
                members[0].Account == members[1].Account || members[0].Date != members[1].Date ||
                members.Any(r => r.Type != "Transfer")))
            {
                issues.Add(new ImportIssue(first.Line, "Transfer ID requires exactly two same-date, different-account rows with opposite equal amounts."));
            }
            else if (kind != "Transfer" && members.Length != 1)
            {
                issues.Add(new ImportIssue(first.Line, "Duplicate ID or ambiguous opening balance."));
            }

            if (kind == "OpeningBalance" && (first.Tags.Length > 0 ||
                rows.Count(r => r is { Type: "Transfer", Description: "Opening balance" } && r.Account == first.Account) != 1))
            {
                issues.Add(new ImportIssue(first.Line, "Ambiguous opening balance; one untagged singleton is required per account."));
            }

            if (kind == "Transfer" && members.Any(r => r.Tags.Length > 0))
            {
                issues.Add(new ImportIssue(first.Line, "Tagged transfers require manual review."));
            }

            var canonical = members
                .OrderBy(r => r.Account, StringComparer.Ordinal)
                .Select(r => r.Fields)
                .ToArray();
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
            groups.Add(new CsvImportGroup(grouping.Key, kind, members, fingerprint));
        }
        return groups;
    }
}
