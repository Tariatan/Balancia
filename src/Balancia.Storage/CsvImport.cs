using Balancia.Core;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    internal const string ReminderIdPrefix = "reminder:";

    /// <summary>CSV source id of a reminder template, shared by export and import.</summary>
    internal static string ReminderSourceId(string templateId) => ReminderIdPrefix + templateId;

    public CsvImportPreview PreviewCsvImport(string importPath) => LogOperation(() => PreviewCsvImportCore(importPath));

    private CsvImportPreview PreviewCsvImportCore(string importPath)
    {
        var fullPath = Path.GetFullPath(importPath);
        var bytes = File.ReadAllBytes(fullPath);
        var (groups, issues, summary) = CsvImportParser.Parse(bytes, Today);
        Log.ForContext<LedgerStore>().Information("CSV preview prepared, Rows: '{Rows}', Issues: '{Issues}'", summary.Rows, issues.Count);
        return new CsvImportPreview(fullPath, HashHex(bytes), ReadLiveMetadata().Revision, [.. groups], [.. issues], summary, ReadExistingLedger());
    }

    private static string HashHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public ImportResult ApplyCsvImport(CsvImportPreview preview) => LogOperation(() => ApplyCsvImportCore(preview));

    /// <summary>
    /// Replaces the whole ledger with the file: accounts, categories, transactions and reminders are deleted and the file's rows are imported
    /// in the same transaction, so a failure leaves the ledger as it was. A recovery copy of a ledger that holds data is saved first.
    /// </summary>
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

        using (var c = connections.Open())
        using (var tx = c.BeginTransaction(deferred: true))
        {
            EnsureRevision(c, tx, preview.Revision);
            tx.Commit();
        }

        var recoveryCopy = preview.Existing.IsEmpty ? null : SaveRecoveryCopy(PreImportCopyKind);
        var imported = 0;
        Write((c, tx) =>
        {
            EnsureRevision(c, tx, preview.Revision);
            ClearLedger(c, tx);

            var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var categories = new List<Category>();
            CreateMissingAccounts(c, tx, preview.Groups, accounts);

            foreach (var group in preview.Groups.OrderBy(g => g.Kind == CsvRowType.OpeningBalance ? 0 : 1))
            {
                switch (group.Kind)
                {
                    case CsvRowType.Reminder:
                        ImportReminder(c, tx, group.Rows[0]);
                        break;
                    case CsvRowType.OpeningBalance:
                        ImportOpeningGroup(c, tx, group, accounts);
                        break;
                    default:
                        ImportTransactionGroup(c, tx, group, accounts, categories);
                        break;
                }

                imported++;
            }

            VerifyImportedTotals(c, tx, preview.Groups, accounts);
        });
        if (recoveryCopy is not null)
        {
            PruneRecoveryCopies(recoveryCopy, PreImportCopyKind);
        }

        Log.ForContext<LedgerStore>().Information("CSV import replaced the ledger, Imported: '{Imported}', Removed: '{Removed}', RecoveryCopy: '{RecoveryCopy}'",
            imported, preview.Existing, recoveryCopy);
        return new ImportResult(imported, recoveryCopy);
    }

    /// <summary>What the ledger holds now, which an import would delete.</summary>
    private ExistingLedger ReadExistingLedger()
    {
        using var connection = connections.Open();
        using var tx = connection.BeginTransaction(deferred: true);
        var existing = new ExistingLedger(
            Convert.ToInt32(LedgerSql.Scalar(connection, tx, "SELECT COUNT(*) FROM accounts")),
            Convert.ToInt32(LedgerSql.Scalar(connection, tx, "SELECT COUNT(*) FROM ledger WHERE kind<>'OpeningBalance'")),
            Convert.ToInt32(LedgerSql.Scalar(connection, tx, "SELECT COUNT(*) FROM reminder_templates")));
        tx.Commit();
        return existing;
    }

    /// <summary>
    /// Deletes every account, category, transaction and reminder, and the import records that databases made before 8.1.1 still hold; the dataset id and
    /// the revision counter stay.
    /// </summary>
    private static void ClearLedger(SqliteConnection c, SqliteTransaction tx) => LedgerSql.Execute(c, tx, """
        DELETE FROM import_sources;
        DELETE FROM movements;
        DELETE FROM ledger;
        DELETE FROM categories;
        DELETE FROM accounts;
        DELETE FROM reminder_templates;
        """);

    private static void EnsureRevision(SqliteConnection c, SqliteTransaction tx, long expected)
    {
        if (Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")) != expected)
        {
            throw new CsvImportConflictException("Ledger changed after preview. Preview the import again.");
        }
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

    private static void ImportOpeningGroup(SqliteConnection c, SqliteTransaction tx, CsvImportGroup group, Dictionary<string, string> accounts)
    {
        var row = group.Rows[0];
        var account = accounts[row.Account];
        var transactionId = OpeningId(account);
        var oldAmount = Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT amount FROM movements WHERE transaction_id=$id", ("$id", transactionId)));
        if (oldAmount != 0)
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
    }

    private static void ImportTransactionGroup(SqliteConnection c, SqliteTransaction tx, CsvImportGroup group, Dictionary<string, string> accounts,
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
    }

    private static void VerifyImportedTotals(SqliteConnection c, SqliteTransaction tx, CsvImportGroup[] groups, Dictionary<string, string> accounts)
    {
        var importedTotals = AccountRows(groups).GroupBy(r => r.Account, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        foreach (var (name, total) in importedTotals)
        {
            var importedSum = Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT COALESCE(SUM(amount),0) FROM movements WHERE account_id=$id", ("$id", accounts[name])));
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

    /// <summary>Imports one reminder template row. Reminders have no ledger transaction.</summary>
    private static void ImportReminder(SqliteConnection c, SqliteTransaction tx, CsvImportRow row)
    {
        var id = row.Id[ReminderIdPrefix.Length..];
        var description = row.Description.Trim();
        var options = JsonSerializer.Deserialize<CsvReminderOptions>(row.Memo)!;
        if (!options.Archived && ReminderRules.IsDuplicateDescription(ReadActiveTemplates(c, tx), id, description))
        {
            throw new CsvImportConflictException(ReminderRules.DuplicateDescriptionMessage);
        }

        LedgerSql.Execute(c, tx, "INSERT INTO reminder_templates VALUES($id,$description,$date,$amount,$interval,$archived)",
            ("$id", id), ("$description", description), ("$date", LedgerSql.DateText(row.Date)),
            ("$amount", row.Amount), ("$interval", options.IntervalMonths), ("$archived", options.Archived ? 1 : 0));
    }
}
