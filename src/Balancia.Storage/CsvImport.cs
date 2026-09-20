using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

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
    { Path = path; FileHash = hash; Revision = revision; Groups = groups; Issues = issues; Summary = summary; }
    public string Path { get; }
    public string FileHash { get; }
    public long Revision { get; }
    internal CsvImportGroup[] Groups { get; }
    public IReadOnlyList<ImportIssue> Issues { get; }
    public ImportSummary Summary { get; }
    public IReadOnlyList<ImportDateExample> ResolvedDates => Groups.SelectMany(g => g.Rows)
        .OrderBy(r => r.Line).Take(10).Select(r => new ImportDateExample(r.Line, r.Date)).ToArray();
    public bool CanApply => Issues.Count == 0;
}

public sealed partial class LedgerStore
{
    private static readonly string[] CsvImportHeader =
        ["ID", "Date", "Description", "Currency", "Amount", "Type", "Tags", "Account", "Status", "Memo", "IOU"];

    public CsvImportPreview PreviewCsvImport(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var issues = new List<ImportIssue>();
        var rows = new List<CsvImportRow>();
        var sourceRows = 0;
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(CsvImportHeader))
                issues.Add(new(1, "Expected the 11 import columns in their original order."));
            if (issues.Count == 0)
            {
                while (!parser.EndOfData)
                {
                    var line = parser.LineNumber;
                    string[]? f;
                    try { f = parser.ReadFields(); }
                    catch (MalformedLineException ex) { issues.Add(new(line, "Malformed CSV quoting: " + ex.Message)); break; }
                    if (f is null) break;
                    sourceRows++;
                    if (f.Length != 11) { issues.Add(new(line, $"Expected 11 columns; found {f.Length}.")); continue; }
                    var errors = new List<string>();
                    if (string.IsNullOrWhiteSpace(f[0])) errors.Add("Missing source ID.");
                    DateOnly date = default;
                    if (f[1].Length != 8 || f[1][2] != '-' || f[1][5] != '-' ||
                        !int.TryParse(f[1][..2], out var day) ||
                        !int.TryParse(f[1].Substring(3, 2), out var month) ||
                        !int.TryParse(f[1][6..], out var year) ||
                        !DateOnly.TryParseExact($"{2000 + year:D4}-{month:D2}-{day:D2}", "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        errors.Add("Date must be DD-MM-YY in 2000–2099.");
                    if (f[3] != "CHF") errors.Add("Only CHF currency is supported.");
                    long amount = 0;
                    if (!decimal.TryParse(f[4], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out var parsed) ||
                        parsed * 100 != decimal.Truncate(parsed * 100) ||
                        parsed * 100 < long.MinValue || parsed * 100 > long.MaxValue)
                        errors.Add("Amount must be an exact CHF centime within Int64 range.");
                    else amount = (long)(parsed * 100);
                    if (f[5] is not ("Expense" or "Income" or "Transfer")) errors.Add("Unsupported transaction type.");
                    if (f[5] == "Expense" && amount >= 0 || f[5] == "Income" && amount <= 0 || amount == 0)
                        errors.Add("Amount sign or zero conflicts with transaction type.");
                    if (string.IsNullOrWhiteSpace(f[7])) errors.Add("Account is required.");
                    if (f[8] != "Cleared") errors.Add("Unsupported status; review before import.");
                    if (!string.IsNullOrWhiteSpace(f[10])) errors.Add("IOU data is unsupported; review before import.");
                    if (f[6].Split('/', StringSplitOptions.None).Length > 2 ||
                        f[6].Split('/').Any(x => f[6].Length > 0 && string.IsNullOrWhiteSpace(x)))
                        errors.Add("Tags must be a standalone category or Parent / Child.");
                    foreach (var error in errors) issues.Add(new(line, error));
                    if (errors.Count == 0) rows.Add(new(line, f[0], date, f[2], amount, f[5], f[6], f[7], f[9], f));
                }
            }
        }
        catch (DecoderFallbackException ex) { issues.Add(new(0, "Invalid UTF-8: " + ex.Message)); }
        catch (MalformedLineException ex) { issues.Add(new(1, "Malformed CSV header: " + ex.Message)); }

        var groups = new List<CsvImportGroup>();
        foreach (var grouping in rows.GroupBy(r => r.Id, StringComparer.Ordinal))
        {
            var members = grouping.ToArray();
            var first = members[0];
            var kind = first.Type;
            if (kind == "Transfer" && members.Length == 1 && first.Description == "Opening balance") kind = "OpeningBalance";
            if (kind == "Transfer" && (members.Length != 2 || members[0].Amount != -members[1].Amount ||
                members[0].Account == members[1].Account || members[0].Date != members[1].Date ||
                members.Any(r => r.Type != "Transfer")))
                issues.Add(new(first.Line, "Transfer ID requires exactly two same-date, different-account rows with opposite equal amounts."));
            else if (kind != "Transfer" && members.Length != 1)
                issues.Add(new(first.Line, "Duplicate ID or ambiguous opening balance."));
            if (kind == "OpeningBalance" && (first.Fields[6].Length > 0 ||
                rows.Count(r => r.Type == "Transfer" && r.Description == "Opening balance" && r.Account == first.Account) != 1))
                issues.Add(new(first.Line, "Ambiguous opening balance; one untagged singleton is required per account."));
            if (kind == "Transfer" && members.Any(r => r.Tags.Length > 0))
                issues.Add(new(first.Line, "Tagged transfers require manual review."));
            var canonical = members.OrderBy(r => r.Account, StringComparer.Ordinal).Select(r => r.Fields).ToArray();
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
            groups.Add(new(grouping.Key, kind, members, fingerprint));
        }
        var duplicateOpenings = groups.Where(g => g.Kind == "OpeningBalance").GroupBy(g => g.Rows[0].Account)
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateOpenings) issues.Add(new(group.First().Rows[0].Line, "Multiple opening balances for one account."));
        var totals = rows.GroupBy(r => r.Account).Select(g => new ImportAccountTotal(g.Key,
            checked((long)g.Sum(r => (decimal)r.Amount)))).OrderBy(x => x.Account).ToArray();
        var categories = rows.Where(r => r.Tags.Length > 0).Select(r => r.Tags.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();
        var summary = new ImportSummary(sourceRows, groups.Count(g => g.Kind == "Expense"),
            groups.Count(g => g.Kind == "Income"), groups.Count(g => g.Kind == "Transfer"),
            groups.Count(g => g.Kind == "OpeningBalance"), totals, categories);
        return new(fullPath, hash, ReadSnapshot().Revision, groups.ToArray(), issues.ToArray(), summary);
    }

    public ImportResult ApplyCsvImport(CsvImportPreview preview)
    {
        if (!preview.CanApply) throw new InvalidOperationException("Resolve preview errors before applying the import.");
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(preview.Path))) != preview.FileHash)
            throw new InvalidOperationException("Source file changed after preview. Preview it again.");
        using (var c = _connections.Open())
        using (var tx = c.BeginTransaction(deferred: true))
        {
            if (Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != preview.Revision)
                throw new InvalidOperationException("Ledger changed after preview. Preview the import again.");
            var matched = 0;
            foreach (var group in preview.Groups)
            {
                using var cmd = Command(c, tx, "SELECT fingerprint,locally_modified FROM import_sources WHERE external_id=$id", ("$id", group.Id));
                using var found = cmd.ExecuteReader();
                if (!found.Read()) continue;
                if (found.GetBoolean(1) || found.GetString(0) != group.Fingerprint)
                    throw new InvalidOperationException($"Source ID {group.Id} conflicts with a prior import or local edit.");
                matched++;
            }
            if (matched == preview.Groups.Length)
            {
                tx.Commit();
                return new(0, matched);
            }
            tx.Commit();
        }
        var added = 0; var unchanged = 0;
        Write((c, tx) =>
        {
            if (Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != preview.Revision)
                throw new InvalidOperationException("Ledger changed after preview. Preview the import again.");
            foreach (var group in preview.Groups)
            {
                using var cmd = Command(c, tx, "SELECT fingerprint,locally_modified FROM import_sources WHERE external_id=$id", ("$id", group.Id));
                using var result = cmd.ExecuteReader();
                if (!result.Read()) continue;
                if (result.GetBoolean(1) || result.GetString(0) != group.Fingerprint)
                    throw new InvalidOperationException($"Source ID {group.Id} conflicts with a prior import or local edit.");
                unchanged++;
            }
            var oldIds = new HashSet<string>();
            using (var cmd = Command(c, tx, "SELECT external_id FROM import_sources"))
            using (var reader = cmd.ExecuteReader()) while (reader.Read()) oldIds.Add(reader.GetString(0));
            if (oldIds.Except(preview.Groups.Select(g => g.Id)).Any())
                throw new InvalidOperationException("This file omits previously imported source IDs; use a full file for reconciliation.");
            var pending = preview.Groups.Where(g => !oldIds.Contains(g.Id)).ToArray();
            var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = Command(c, tx, "SELECT id,name FROM accounts"))
            using (var reader = cmd.ExecuteReader()) while (reader.Read()) accounts.Add(reader.GetString(1), reader.GetString(0));
            var existingAccounts = new HashSet<string>(accounts.Keys, StringComparer.OrdinalIgnoreCase);
            var earliest = pending.SelectMany(g => g.Rows).GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(r => r.Date), StringComparer.OrdinalIgnoreCase);
            foreach (var (name, firstDate) in earliest)
            {
                if (accounts.ContainsKey(name)) continue;
                var id = Guid.NewGuid().ToString("N"); accounts.Add(name, id);
                Execute(c, tx, "INSERT INTO accounts VALUES($id,$name,$date,0); INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,''); INSERT INTO movements VALUES($opening,$id,0)",
                    ("$id", id), ("$name", name), ("$date", DateText(firstDate)), ("$opening", "opening:" + id));
            }
            foreach (var group in pending.OrderBy(g => g.Kind == "OpeningBalance" ? 0 : 1))
            {
                var row = group.Rows[0];
                string transactionId;
                if (group.Kind == "OpeningBalance")
                {
                    var account = accounts[row.Account]; transactionId = "opening:" + account;
                    var oldAmount = Convert.ToInt64(Scalar(c, tx, "SELECT amount FROM movements WHERE transaction_id=$id", ("$id", transactionId)));
                    var hasEntries = Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>$opening LIMIT 1", ("$id", account), ("$opening", transactionId)) is not null;
                    if (oldAmount != 0 || hasEntries && existingAccounts.Contains(row.Account))
                        throw new InvalidOperationException($"Opening for {row.Account} conflicts with existing account data.");
                    var priorDate = ParseDate((string)Scalar(c, tx, "SELECT opening_date FROM accounts WHERE id=$id", ("$id", account))!);
                    if (row.Date > priorDate) throw new InvalidOperationException($"Opening for {row.Account} follows existing account history.");
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
                if (importedSum != total) throw new InvalidOperationException($"Imported balance reconciliation failed for {name}.");
            }
        });
        return new(added, unchanged);
    }

    private static string? FindOrCreateCategory(SqliteConnection c, SqliteTransaction tx, string tag)
    {
        if (tag.Length == 0) return null;
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
