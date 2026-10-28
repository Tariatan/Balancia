using Balancia.Core;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    internal const string ReminderIdPrefix = "reminder:";

    /// <summary>CSV source id of a reminder template, shared by export, import and local-edit tracking.</summary>
    internal static string ReminderSourceId(string templateId) => ReminderIdPrefix + templateId;

    public CsvImportPreview PreviewCsvImport(string importPath) => LogOperation(() => PreviewCsvImportCore(importPath));

    private CsvImportPreview PreviewCsvImportCore(string importPath)
    {
        var fullPath = Path.GetFullPath(importPath);
        var bytes = File.ReadAllBytes(fullPath);
        var (groups, issues, summary) = CsvImportParser.Parse(bytes, Today);
        Log.ForContext<LedgerStore>().Information("CSV preview prepared, Rows: '{Rows}', Issues: '{Issues}'", summary.Rows, issues.Count);
        return new CsvImportPreview(fullPath, HashHex(bytes), ReadLiveMetadata().Revision, [.. groups], [.. issues], summary);
    }

    private static string HashHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public ImportResult ApplyCsvImport(CsvImportPreview preview) => LogOperation(() => ApplyCsvImportCore(preview));

    private ImportResult ApplyCsvImportCore(CsvImportPreview preview)
    {
        if (!preview.CanApply)
        {
            throw new CsvImportConflictException("Resolve preview errors before applying the import.");
        }

        if (HashHex(File.ReadAllBytes(preview.Path)) != preview.FileHash)
        {
            throw new CsvImportConflictException("Source file changed after preview. Preview it again.");
        }

        int matched;
        using (var c = connections.Open())
        using (var tx = c.BeginTransaction(deferred: true))
        {
            EnsureRevision(c, tx, preview.Revision);
            matched = CountMatchingSources(LoadImportSources(c, tx), preview.Groups);
            tx.Commit();
        }

        if (matched == preview.Groups.Length)
        {
            Log.ForContext<LedgerStore>().Information("CSV reconciliation unchanged, Entries: '{Entries}'", matched);
            return new ImportResult(0, matched);
        }

        var added = 0;
        var unchanged = 0;
        Write((c, tx) =>
        {
            EnsureRevision(c, tx, preview.Revision);

            var sources = LoadImportSources(c, tx);
            unchanged = CountMatchingSources(sources, preview.Groups);
            if (sources.Keys.Except(preview.Groups.Select(g => g.Id)).Any())
            {
                throw new CsvImportConflictException("This file omits previously imported source IDs; use a full file for reconciliation.");
            }

            var pending = preview.Groups.Where(g => !sources.ContainsKey(g.Id)).ToArray();
            var accounts = LoadAccounts(c, tx);
            var categories = ReadCategories(c, tx);
            var existingAccounts = new HashSet<string>(accounts.Keys, StringComparer.OrdinalIgnoreCase);
            CreateMissingAccounts(c, tx, pending, accounts);

            foreach (var group in pending.OrderBy(g => g.Kind == CsvRowType.OpeningBalance ? 0 : 1))
            {
                string? transactionId = group.Kind switch
                {
                    CsvRowType.Reminder => ImportReminder(c, tx, group.Rows[0]),
                    CsvRowType.OpeningBalance => ImportOpeningGroup(c, tx, group, accounts, existingAccounts),
                    _ => ImportTransactionGroup(c, tx, group, accounts, categories),
                };
                LedgerSql.Execute(c, tx, "INSERT INTO import_sources(source,external_id,transaction_id,fingerprint,raw_rows_json) VALUES('CSV',$external,$transaction,$fingerprint,$raw)",
                    ("$external", group.Id), ("$transaction", transactionId), ("$fingerprint", group.Fingerprint),
                    ("$raw", JsonSerializer.Serialize(group.Rows.Select(r => r.Fields).Distinct().ToArray())));
                added++;
            }

            VerifyImportedTotals(c, tx, preview.Groups, accounts);
        });
        Log.ForContext<LedgerStore>().Information("CSV reconciliation committed, Added: '{Added}', Unchanged: '{Unchanged}'", added, unchanged);
        return new ImportResult(added, unchanged);
    }

    private static void EnsureRevision(SqliteConnection c, SqliteTransaction tx, long expected)
    {
        if (Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != expected)
        {
            throw new CsvImportConflictException("Ledger changed after preview. Preview the import again.");
        }
    }

    private static Dictionary<string, (string Fingerprint, bool LocallyModified)> LoadImportSources(SqliteConnection c, SqliteTransaction tx)
    {
        var sources = new Dictionary<string, (string, bool)>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT external_id,fingerprint,locally_modified FROM import_sources");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sources[reader.GetString(0)] = (reader.GetString(1), reader.GetBoolean(2));
        }
        return sources;
    }

    private static Dictionary<string, string> LoadAccounts(SqliteConnection c, SqliteTransaction tx)
    {
        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = LedgerSql.Command(c, tx, "SELECT id,name FROM accounts");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(reader.GetString(1), reader.GetString(0));
        }
        return accounts;
    }

    private static int CountMatchingSources(Dictionary<string, (string Fingerprint, bool LocallyModified)> sources, CsvImportGroup[] groups)
    {
        var matched = 0;
        foreach (var group in groups)
        {
            if (!sources.TryGetValue(group.Id, out var source))
            {
                continue;
            }

            if (source.LocallyModified || source.Fingerprint != group.Fingerprint)
            {
                throw new CsvImportConflictException($"Source ID {group.Id} conflicts with a prior import or local edit.");
            }

            matched++;
        }
        return matched;
    }

    private static void CreateMissingAccounts(SqliteConnection c, SqliteTransaction tx, CsvImportGroup[] pending, Dictionary<string, string> accounts)
    {
        var earliest = AccountRows(pending).GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Date), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, firstDate) in earliest)
        {
            if (accounts.ContainsKey(name))
            {
                continue;
            }

            var id = Guid.NewGuid().ToString("N");
            accounts.Add(name, id);
            LedgerSql.Execute(c, tx, "INSERT INTO accounts VALUES($id,$name,$date,0); INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,''); INSERT INTO movements VALUES($opening,$id,0)",
                ("$id", id), ("$name", name), ("$date", LedgerSql.DateText(firstDate)), ("$opening", OpeningId(id)));
        }
    }

    /// <summary>Rows that move money on an account; reminders carry no account.</summary>
    private static IEnumerable<CsvImportRow> AccountRows(IEnumerable<CsvImportGroup> groups) =>
        groups.Where(g => g.Kind != CsvRowType.Reminder).SelectMany(g => g.Rows);

    private static string ImportOpeningGroup(SqliteConnection c, SqliteTransaction tx, CsvImportGroup group,
        Dictionary<string, string> accounts, HashSet<string> existingAccounts)
    {
        var row = group.Rows[0];
        var account = accounts[row.Account];
        var transactionId = OpeningId(account);
        var oldAmount = Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT amount FROM movements WHERE transaction_id=$id", ("$id", transactionId)));
        var hasEntries = LedgerSql.Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>$opening LIMIT 1", ("$id", account), ("$opening", transactionId)) is not null;
        if (oldAmount != 0 || hasEntries && existingAccounts.Contains(row.Account))
        {
            throw new CsvImportConflictException($"Opening for {row.Account} conflicts with existing account data.");
        }

        var priorDate = LedgerSql.ParseDate((string)LedgerSql.Scalar(c, tx, "SELECT opening_date FROM accounts WHERE id=$id", ("$id", account))!);
        if (row.Date > priorDate)
        {
            throw new CsvImportConflictException($"Opening for {row.Account} follows existing account history.");
        }

        LedgerSql.Execute(c, tx, "UPDATE accounts SET opening_date=$date WHERE id=$id; UPDATE ledger SET date=$date WHERE id=$opening; UPDATE movements SET amount=$amount WHERE transaction_id=$opening",
            ("$date", LedgerSql.DateText(row.Date)), ("$id", account), ("$opening", transactionId), ("$amount", row.Amount));
        return transactionId;
    }

    private static string ImportTransactionGroup(SqliteConnection c, SqliteTransaction tx, CsvImportGroup group, Dictionary<string, string> accounts,
        List<Category> categories)
    {
        var row = group.Rows[0];
        var transactionId = Guid.NewGuid().ToString("N");
        var category = group.Kind == CsvRowType.Transfer ? null : FindOrCreateCategory(c, tx, categories, row.Category);
        LedgerSql.Execute(c, tx, "INSERT INTO ledger VALUES($id,$kind,$date,$description,$category,$memo)",
            ("$id", transactionId), ("$kind", group.Kind.ToString()), ("$date", LedgerSql.DateText(row.Date)),
            ("$description", row.Description.Trim()), ("$category", category), ("$memo", row.Memo));
        foreach (var movement in group.Rows)
        {
            ValidateAccount(c, tx, accounts[movement.Account], movement.Date, null);
            AddMovement(c, tx, transactionId, accounts[movement.Account], movement.Amount);
        }
        return transactionId;
    }

    private static void VerifyImportedTotals(SqliteConnection c, SqliteTransaction tx, CsvImportGroup[] groups, Dictionary<string, string> accounts)
    {
        var importedTotals = AccountRows(groups).GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        foreach (var (name, total) in importedTotals)
        {
            var importedSum = Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT COALESCE(SUM(m.amount),0) FROM movements m JOIN import_sources s ON s.transaction_id=m.transaction_id WHERE m.account_id=$id", ("$id", accounts[name])));
            if (importedSum != total)
            {
                throw new CsvImportConflictException($"Imported balance reconciliation failed for {name}.");
            }
        }
    }

    /// <summary>
    /// The category a row's path names, created when it does not exist yet. Names are matched with <see cref="CategoryRules.FindChild"/> over
    /// <paramref name="categories"/>, which holds every category and receives the ones created here so that later rows find them.
    /// </summary>
    private static string? FindOrCreateCategory(SqliteConnection c, SqliteTransaction tx, List<Category> categories, string categoryPath)
    {
        if (!CategoryRules.TryParsePath(categoryPath, out var names))
        {
            throw new ArgumentException(CategoryRules.InvalidPathMessage);
        }

        string? parent = null;
        for (var level = 0; level < names.Length; level++)
        {
            var name = names[level];
            var found = CategoryRules.FindChild(categories, parent, name);
            if (found is { Archived: true })
            {
                throw new CsvImportConflictException($"Category '{name}' is archived; restore it before importing.");
            }

            if (found is null)
            {
                found = new Category(Guid.NewGuid().ToString("N"), name, parent, string.Join(" / ", names.Take(level + 1)), false);
                LedgerSql.Execute(c, tx, "INSERT INTO categories VALUES($id,$name,$parent,0)", ("$id", found.Id), ("$name", name), ("$parent", parent));
                categories.Add(found);
            }

            parent = found.Id;
        }

        return parent;
    }

    /// <summary>Imports one reminder template row; an identical existing template is left untouched. Reminders have no ledger transaction, so the result is always null.</summary>
    private static string? ImportReminder(SqliteConnection c, SqliteTransaction tx, CsvImportRow row)
    {
        var id = row.Id[ReminderIdPrefix.Length..];
        var description = row.Description.Trim();
        var options = JsonSerializer.Deserialize<CsvReminderOptions>(row.Memo)!;
        using (var command = LedgerSql.Command(c, tx,
            "SELECT description,expected_date,indicative_amount,interval_months,archived FROM reminder_templates WHERE id=$id", ("$id", id)))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(0) != description || LedgerSql.ParseDate(reader.GetString(1)) != row.Date ||
                    reader.GetInt64(2) != row.Amount || reader.GetInt32(3) != options.IntervalMonths ||
                    reader.GetBoolean(4) != options.Archived)
                {
                    throw new CsvImportConflictException("Imported reminder conflicts with existing reminder data.");
                }

                return null;
            }
        }

        if (!options.Archived && ReminderRules.IsDuplicateDescription(ReadActiveTemplates(c, tx), id, description))
        {
            throw new CsvImportConflictException(ReminderRules.DuplicateDescriptionMessage);
        }

        LedgerSql.Execute(c, tx, "INSERT INTO reminder_templates VALUES($id,$description,$date,$amount,$interval,$archived)",
            ("$id", id), ("$description", description), ("$date", LedgerSql.DateText(row.Date)),
            ("$amount", row.Amount), ("$interval", options.IntervalMonths), ("$archived", options.Archived ? 1 : 0));
        return null;
    }
}
