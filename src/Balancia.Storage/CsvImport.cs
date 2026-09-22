using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed record ImportIssue(long Line, string Message);
public sealed record ImportAccountTotal(string Account, long Centimes);
public sealed record ImportDateExample(long Line, DateOnly Date);
public sealed record ImportSummary(int Rows, int Expenses, int Incomes, int Transfers, int Openings,
    IReadOnlyList<ImportAccountTotal> AccountTotals, IReadOnlyList<string> Categories);
public sealed record ImportResult(int Added, int Unchanged);

internal sealed record CsvImportRow(long Line, string Id, DateOnly Date, string Description, long Amount,
    string Type, string Tags, string Account, string Memo, string[] Fields);
internal sealed record CsvImportGroup(string Id, string Kind, CsvImportRow[] Rows, string Fingerprint);

public sealed class CsvImportPreview
{
    internal CsvImportPreview(string path, string hash, long revision, CsvImportGroup[] groups,
        ImportIssue[] issues, ImportSummary summary)
    {
        Path = path;
        FileHash = hash;
        Revision = revision;
        Groups = groups;
        Issues = issues;
        Summary = summary;
    }
    public string Path
    {
        get;
    }
    public string FileHash
    {
        get;
    }
    public long Revision
    {
        get;
    }
    internal CsvImportGroup[] Groups
    {
        get;
    }
    public IReadOnlyList<ImportIssue> Issues
    {
        get;
    }
    public ImportSummary Summary
    {
        get;
    }
    public IReadOnlyList<ImportDateExample> ResolvedDates =>
    [
        .. Groups.SelectMany(g => g.Rows)
            .OrderBy(r => r.Line).Take(10).Select(r => new ImportDateExample(r.Line, r.Date))
    ];
    public bool CanApply => Issues.Count == 0;
}

public sealed partial class LedgerStore
{
    public CsvImportPreview PreviewCsvImport(string importPath)
    {
        var fullPath = Path.GetFullPath(importPath);
        var bytes = File.ReadAllBytes(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var (groups, issues, summary) = CsvImportParser.Parse(bytes);
        return new CsvImportPreview(fullPath, hash, ReadSnapshot().Revision, groups.ToArray(), issues.ToArray(), summary);
    }

    public ImportResult ApplyCsvImport(CsvImportPreview preview)
    {
        if (!preview.CanApply)
        {
            throw new InvalidOperationException("Resolve preview errors before applying the import.");
        }

        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(preview.Path))) != preview.FileHash)
        {
            throw new InvalidOperationException("Source file changed after preview. Preview it again.");
        }

        using (var c = connections.Open())
        using (var tx = c.BeginTransaction(deferred: true))
        {
            if (Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != preview.Revision)
            {
                throw new InvalidOperationException("Ledger changed after preview. Preview the import again.");
            }

            var matched = CountMatchingSources(c, tx, preview.Groups);
            if (matched == preview.Groups.Length)
            {
                tx.Commit();
                return new ImportResult(0, matched);
            }
            tx.Commit();
        }
        var added = 0;
        var unchanged = 0;
        Write((c, tx) =>
        {
            if (Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != preview.Revision)
            {
                throw new InvalidOperationException("Ledger changed after preview. Preview the import again.");
            }

            unchanged = CountMatchingSources(c, tx, preview.Groups);
            var oldIds = new HashSet<string>();
            using (var cmd = Command(c, tx, "SELECT external_id FROM import_sources"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    oldIds.Add(reader.GetString(0));
                }
            }

            if (oldIds.Except(preview.Groups.Select(g => g.Id)).Any())
            {
                throw new InvalidOperationException("This file omits previously imported source IDs; use a full file for reconciliation.");
            }

            var pending = preview.Groups.Where(g => !oldIds.Contains(g.Id)).ToArray();
            var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = Command(c, tx, "SELECT id,name FROM accounts"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    accounts.Add(reader.GetString(1), reader.GetString(0));
                }
            }

            var existingAccounts = new HashSet<string>(accounts.Keys, StringComparer.OrdinalIgnoreCase);
            var earliest = pending.SelectMany(g => g.Rows).GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(r => r.Date), StringComparer.OrdinalIgnoreCase);
            foreach (var (name, firstDate) in earliest)
            {
                if (accounts.ContainsKey(name))
                {
                    continue;
                }

                var id = Guid.NewGuid().ToString("N");
                accounts.Add(name, id);
                Execute(c, tx, "INSERT INTO accounts VALUES($id,$name,$date,0); INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,''); INSERT INTO movements VALUES($opening,$id,0)",
                    ("$id", id), ("$name", name), ("$date", DateText(firstDate)), ("$opening", "opening:" + id));
            }
            foreach (var group in pending.OrderBy(g => g.Kind == "OpeningBalance" ? 0 : 1))
            {
                var row = group.Rows[0];
                string transactionId;
                if (group.Kind == "OpeningBalance")
                {
                    var account = accounts[row.Account];
                    transactionId = "opening:" + account;
                    var oldAmount = Convert.ToInt64(Scalar(c, tx, "SELECT amount FROM movements WHERE transaction_id=$id", ("$id", transactionId)));
                    var hasEntries = Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>$opening LIMIT 1", ("$id", account), ("$opening", transactionId)) is not null;
                    if (oldAmount != 0 || hasEntries && existingAccounts.Contains(row.Account))
                    {
                        throw new InvalidOperationException($"Opening for {row.Account} conflicts with existing account data.");
                    }

                    var priorDate = ParseDate((string)Scalar(c, tx, "SELECT opening_date FROM accounts WHERE id=$id", ("$id", account))!);
                    if (row.Date > priorDate)
                    {
                        throw new InvalidOperationException($"Opening for {row.Account} follows existing account history.");
                    }

                    Execute(c, tx, "UPDATE accounts SET opening_date=$date WHERE id=$id; UPDATE ledger SET date=$date WHERE id=$opening; UPDATE movements SET amount=$amount WHERE transaction_id=$opening",
                        ("$date", DateText(row.Date)), ("$id", account), ("$opening", transactionId), ("$amount", row.Amount));
                }
                else
                {
                    transactionId = Guid.NewGuid().ToString("N");
                    var category = group.Kind == "Transfer" ? null : FindOrCreateCategory(c, tx, row.Tags);
                    Execute(c, tx, "INSERT INTO ledger VALUES($id,$kind,$date,$description,$category,$memo)",
                        ("$id", transactionId), ("$kind", group.Kind), ("$date", DateText(row.Date)),
                        ("$description", row.Description), ("$category", category), ("$memo", row.Memo));
                    foreach (var movement in group.Rows)
                    {
                        ValidateAccount(c, tx, accounts[movement.Account], movement.Date, null);
                        AddMovement(c, tx, transactionId, accounts[movement.Account], movement.Amount);
                    }
                }
                Execute(c, tx, "INSERT INTO import_sources(source,external_id,transaction_id,fingerprint,raw_rows_json) VALUES('CSV',$external,$transaction,$fingerprint,$raw)",
                    ("$external", group.Id), ("$transaction", transactionId), ("$fingerprint", group.Fingerprint),
                    ("$raw", JsonSerializer.Serialize(group.Rows.Select(r => r.Fields).ToArray())));
                added++;
            }
            var importedTotals = preview.Groups.SelectMany(g => g.Rows).GroupBy(r => r.Account)
                .ToDictionary(g => g.Key, g => checked((long)g.Sum(r => (decimal)r.Amount)));
            foreach (var (name, total) in importedTotals)
            {
                var importedSum = Convert.ToDecimal(Scalar(c, tx, "SELECT COALESCE(SUM(m.amount),0) FROM movements m JOIN import_sources s ON s.transaction_id=m.transaction_id WHERE m.account_id=$id", ("$id", accounts[name])));
                if (importedSum != total)
                {
                    throw new InvalidOperationException($"Imported balance reconciliation failed for {name}.");
                }
            }
        });
        return new ImportResult(added, unchanged);
    }

    private static int CountMatchingSources(SqliteConnection c, SqliteTransaction tx, CsvImportGroup[] groups)
    {
        var matched = 0;
        foreach (var group in groups)
        {
            using var cmd = Command(c, tx, "SELECT fingerprint,locally_modified FROM import_sources WHERE external_id=$id", ("$id", group.Id));
            using var result = cmd.ExecuteReader();
            if (!result.Read())
            {
                continue;
            }

            if (result.GetBoolean(1) || result.GetString(0) != group.Fingerprint)
            {
                throw new InvalidOperationException($"Source ID {group.Id} conflicts with a prior import or local edit.");
            }

            matched++;
        }
        return matched;
    }

    private static string? FindOrCreateCategory(SqliteConnection c, SqliteTransaction tx, string tag)
    {
        if (tag.Length == 0)
        {
            return null;
        }

        string? parent = null;
        foreach (var name in tag.Split('/').Select(x => x.Trim()))
        {
            var found = Scalar(c, tx, "SELECT id FROM categories WHERE name=$name COLLATE NOCASE AND parent_id IS $parent", ("$name", name), ("$parent", parent)) as string;
            if (found is null)
            {
                found = Guid.NewGuid().ToString("N");
                Execute(c, tx, "INSERT INTO categories VALUES($id,$name,$parent,0)", ("$id", found), ("$name", name), ("$parent", parent));
            }
            parent = found;
        }
        return parent;
    }
}
