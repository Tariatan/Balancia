using System.Globalization;
using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Owns the single-writer ledger boundary; all logical changes commit together.</summary>
public sealed partial class LedgerStore(string path, TimeProvider? clock = null)
{
    private readonly string path = path;
    private readonly SqliteConnectionFactory connections = new(path);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public void Initialize()
    {
        using var c = connections.Open();
        using (var check = c.CreateCommand())
        {
            check.CommandText = "PRAGMA user_version";
            var existing = Convert.ToInt64(check.ExecuteScalar());
            if (existing == 1)
            {
                var backupPath = path + ".pre-v2-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                using var backup = new SqliteConnectionFactory(backupPath).Open();
                c.BackupDatabase(backup);
            }
        }
        using var tx = c.BeginTransaction();
        var version = Convert.ToInt64(Scalar(c, tx, "PRAGMA user_version"));
        if (version > 3)
        {
            throw new InvalidOperationException("This database version is not supported. Use a compatible Balancia version.");
        }

        if (version == 0)
        {
            if (Convert.ToInt64(Scalar(c, tx, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0)
            {
                throw new InvalidOperationException("The selected file is not an empty Balancia database.");
            }

            Execute(c, tx, """
                CREATE TABLE accounts (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(name))>0),
                    opening_date TEXT NOT NULL, archived INTEGER NOT NULL CHECK(archived IN (0,1)));
                CREATE TABLE categories (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL CHECK(length(trim(name))>0),
                    parent_id TEXT REFERENCES categories(id), archived INTEGER NOT NULL CHECK(archived IN (0,1)),
                    CHECK(parent_id IS NULL OR parent_id <> id));
                CREATE UNIQUE INDEX category_names ON categories(COALESCE(parent_id,''), name COLLATE NOCASE);
                CREATE TABLE ledger (
                    id TEXT PRIMARY KEY, kind TEXT NOT NULL CHECK(kind IN ('Expense','Income','Transfer','OpeningBalance')),
                    date TEXT NOT NULL, description TEXT NOT NULL, category_id TEXT REFERENCES categories(id), memo TEXT NOT NULL);
                CREATE TABLE movements (
                    transaction_id TEXT NOT NULL REFERENCES ledger(id) ON DELETE CASCADE,
                    account_id TEXT NOT NULL REFERENCES accounts(id), amount INTEGER NOT NULL CHECK(typeof(amount)='integer'),
                    PRIMARY KEY(transaction_id,account_id));
                CREATE INDEX ledger_dates ON ledger(date DESC,id);
                CREATE INDEX movement_accounts ON movements(account_id,transaction_id);
                CREATE TABLE metadata (id INTEGER PRIMARY KEY CHECK(id=1), dataset_id TEXT NOT NULL,
                    revision INTEGER NOT NULL CHECK(typeof(revision)='integer' AND revision>=0));
                INSERT INTO metadata VALUES(1, $dataset, 0);
                PRAGMA user_version=2;
                """, ("$dataset", Guid.NewGuid().ToString("N")));
            version = 1;
        }

        if (version == 1)
        {
            CreateImportTable(c, tx);
            Execute(c, tx, "PRAGMA user_version=2");
            version = 2;
        }

        if (version == 2)
        {
            CreateRecurringTable(c, tx);
            Execute(c, tx, "PRAGMA user_version=3");
        }

        tx.Commit();
    }

    private static void CreateImportTable(SqliteConnection c, SqliteTransaction tx) => Execute(c, tx, """
        CREATE TABLE import_sources (
            source TEXT NOT NULL, external_id TEXT NOT NULL,
            transaction_id TEXT REFERENCES ledger(id) ON DELETE SET NULL,
            fingerprint TEXT NOT NULL, raw_rows_json TEXT NOT NULL,
            locally_modified INTEGER NOT NULL DEFAULT 0 CHECK(locally_modified IN (0,1)),
            PRIMARY KEY(source,external_id));
        CREATE UNIQUE INDEX import_transaction ON import_sources(transaction_id);
        """);

    private static void CreateRecurringTable(SqliteConnection c, SqliteTransaction tx) => Execute(c, tx, """
        CREATE TABLE IF NOT EXISTS recurring_templates (
            id TEXT PRIMARY KEY, description TEXT NOT NULL CHECK(length(trim(description))>0),
            expected_date TEXT NOT NULL, indicative_amount INTEGER NOT NULL CHECK(indicative_amount>0),
            interval_months INTEGER NOT NULL CHECK(interval_months>0), archived INTEGER NOT NULL CHECK(archived IN (0,1)));
        """);

    public IReadOnlyList<RecurringReminder> ReadRecurringReminders(DateOnly? asOf = null)
    {
        var today = asOf ?? Today;
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = new List<RecurringReminder>();
        using var cmd = Command(c, tx, "SELECT id,description,expected_date,indicative_amount,interval_months,archived FROM recurring_templates WHERE archived=0 ORDER BY expected_date,description COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var template = new RecurringTemplate(r.GetString(0), r.GetString(1), ParseDate(r.GetString(2)), new Money(r.GetInt64(3)), r.GetInt32(4), r.GetBoolean(5));
            var occurrence = template.ExpectedDate;
            while (occurrence < today && IsSatisfied(c, tx, template.Description, occurrence))
            {
                occurrence = AddMonthsClamped(occurrence, template.IntervalMonths, template.ExpectedDate.Day);
            }

            result.Add(new RecurringReminder(template, occurrence, occurrence < today, IsSatisfied(c, tx, template.Description, occurrence)));
        }
        tx.Commit();
        return result.OrderBy(x => x.Satisfied ? 1 : 0).ThenBy(x => x.Occurrence).ToArray();
    }

    public string SaveRecurringTemplate(string? id, string description, DateOnly expectedDate, Money indicativeAmount, int intervalMonths, bool archived = false)
    {
        description = description.Trim();
        if (description.Length == 0)
        {
            throw new ArgumentException("Enter a description.");
        }

        if (indicativeAmount <= Money.Zero)
        {
            throw new ArgumentException("Enter amount greater than zero.");
        }

        if (intervalMonths <= 0)
        {
            throw new ArgumentException("Repeat interval must be positive.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (!archived && Scalar(c, tx, "SELECT 1 FROM recurring_templates WHERE archived=0 AND lower(description)=lower($description) AND id<>$id LIMIT 1", ("$description", description), ("$id", key)) is not null)
            {
                throw new ArgumentException("An active recurring template already uses this description.");
            }

            Execute(c, tx, "INSERT INTO recurring_templates VALUES($id,$description,$date,$amount,$interval,$archived) ON CONFLICT(id) DO UPDATE SET description=excluded.description,expected_date=excluded.expected_date,indicative_amount=excluded.indicative_amount,interval_months=excluded.interval_months,archived=excluded.archived", ("$id", key), ("$description", description), ("$date", DateText(expectedDate)), ("$amount", indicativeAmount.Centimes), ("$interval", intervalMonths), ("$archived", archived ? 1 : 0));
        });
        return key;
    }

    public void DeleteRecurringTemplate(string id) => Write((c, tx) => Execute(c, tx, "DELETE FROM recurring_templates WHERE id=$id", ("$id", id)));

    private static bool IsSatisfied(SqliteConnection c, SqliteTransaction tx, string description, DateOnly occurrence) =>
        Scalar(c, tx, "SELECT 1 FROM ledger WHERE kind<>'OpeningBalance' AND description=$description AND substr(date,1,7)=$month LIMIT 1", ("$description", description), ("$month", occurrence.ToString("yyyy-MM", CultureInfo.InvariantCulture))) is not null;

    private static DateOnly AddMonthsClamped(DateOnly date, int months, int desiredDay)
    {
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(first.Year, first.Month, Math.Min(desiredDay, DateTime.DaysInMonth(first.Year, first.Month)));
    }

    public string SaveAccount(string? id, string name, DateOnly openingDate, Money opening, bool archived = false)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Enter an account name.");
        }

        if (openingDate > Today)
        {
            throw new ArgumentException("Opening date cannot be in the future.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
            }

            var earliest = Scalar(c, tx, "SELECT MIN(l.date) FROM ledger l JOIN movements m ON m.transaction_id=l.id WHERE m.account_id=$id AND l.kind<>'OpeningBalance'", ("$id", key));
            if (earliest is string date && openingDate > ParseDate(date))
            {
                throw new ArgumentException("Opening date must be on or before the first transaction in this account.");
            }

            Execute(c, tx, """
                INSERT INTO accounts VALUES($id,$name,$date,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name, opening_date=excluded.opening_date, archived=excluded.archived;
                INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,'')
                ON CONFLICT(id) DO UPDATE SET date=excluded.date;
                INSERT INTO movements VALUES($opening,$id,$amount)
                ON CONFLICT(transaction_id,account_id) DO UPDATE SET amount=excluded.amount;
                """, ("$id", key), ("$name", name), ("$date", DateText(openingDate)),
                ("$archived", archived ? 1 : 0), ("$opening", "opening:" + key), ("$amount", opening.Centimes));
            if (id is not null)
            {
                Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", "opening:" + key));
            }
        });
        return key;
    }

    public void DeleteAccount(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
        if (Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>'opening:' || $id LIMIT 1", ("$id", id)) is not null)
        {
            throw new InvalidOperationException("An account with transactions cannot be deleted. Archive it instead.");
        }

        Execute(c, tx, "DELETE FROM movements WHERE account_id=$id AND transaction_id='opening:' || $id; DELETE FROM ledger WHERE id='opening:' || $id; DELETE FROM accounts WHERE id=$id", ("$id", id));
    });

    public string SaveCategory(string? id, string name, string? parentId, bool archived = false)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Contains('/'))
        {
            throw new ArgumentException("Enter a category name without '/'; choose its parent separately.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", id, "Category no longer exists.");
            }

            if (parentId is not null)
            {
                if (parentId == key)
                {
                    throw new ArgumentException("A category cannot be its own parent.");
                }

                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id AND parent_id IS NULL", parentId, "Choose a top-level parent category.");
                var parentArchived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", parentId))) != 0;
                var previousParent = id is null ? null : Scalar(c, tx, "SELECT parent_id FROM categories WHERE id=$id", ("$id", id)) as string;
                if (parentArchived && (!archived || previousParent != parentId))
                {
                    throw new ArgumentException("Restore the parent category before adding or restoring a child.");
                }

                if (Scalar(c, tx, "SELECT 1 FROM categories WHERE parent_id=$id LIMIT 1", ("$id", key)) is not null)
                {
                    throw new ArgumentException("A category with subcategories must remain top-level.");
                }
            }
            Execute(c, tx, """
                INSERT INTO categories VALUES($id,$name,$parent,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name,parent_id=excluded.parent_id,archived=excluded.archived;
                """, ("$id", key), ("$name", name), ("$parent", parentId), ("$archived", archived ? 1 : 0));
            if (archived)
            {
                Execute(c, tx, "UPDATE categories SET archived=1 WHERE parent_id=$id", ("$id", key));
            }
        });
        return key;
    }

    public void DeleteCategory(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", id, "Category no longer exists.");
        if (Scalar(c, tx, "SELECT 1 FROM categories WHERE parent_id=$id LIMIT 1", ("$id", id)) is not null)
        {
            throw new InvalidOperationException("A category with subcategories cannot be deleted. Archive it instead.");
        }

        if (Scalar(c, tx, "SELECT 1 FROM ledger WHERE category_id=$id LIMIT 1", ("$id", id)) is not null)
        {
            throw new InvalidOperationException("A category used by transactions cannot be deleted. Archive it instead.");
        }

        Execute(c, tx, "DELETE FROM categories WHERE id=$id", ("$id", id));
    });

    public string SaveTransaction(string? id, TransactionDraft draft) => SaveTransactionCore(id, draft, null);

    public string SaveTransactionWithCategoryPath(string? id, TransactionDraft draft, string? categoryPath)
    {
        if (draft.CategoryId is not null)
        {
            throw new ArgumentException("Provide either a category path or a category ID, not both.");
        }

        if (draft.Kind == TransactionKind.Transfer && !string.IsNullOrWhiteSpace(categoryPath))
        {
            throw new ArgumentException("Transfers do not have expense categories.");
        }

        var parts = ParseCategoryPath(categoryPath);
        return SaveTransactionCore(id, draft, parts);
    }

    private string SaveTransactionCore(string? id, TransactionDraft draft, string[]? categoryParts)
    {
        draft.Validate(Today);
        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'", id, "Transaction no longer exists.");
            }

            ValidateAccount(c, tx, draft.AccountId, draft.Date, id);
            if (draft.DestinationId is not null)
            {
                ValidateAccount(c, tx, draft.DestinationId, draft.Date, id);
            }

            if (categoryParts is not null)
            {
                draft = draft with
                {
                    CategoryId = ResolveCategoryPath(c, tx, id, categoryParts)
                };
            }

            if (draft.CategoryId is not null)
            {
                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", draft.CategoryId, "Category no longer exists.");
                var archived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", draft.CategoryId))) != 0;
                var previousCategory = id is null ? null : Scalar(c, tx, "SELECT category_id FROM ledger WHERE id=$id", ("$id", id)) as string;
                if (archived && previousCategory != draft.CategoryId)
                {
                    throw new ArgumentException("Choose an active category.");
                }
            }
            Execute(c, tx, """
                INSERT INTO ledger VALUES($id,$kind,$date,$description,$category,$memo)
                ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,date=excluded.date,description=excluded.description,
                    category_id=excluded.category_id,memo=excluded.memo;
                DELETE FROM movements WHERE transaction_id=$id;
                """, ("$id", key), ("$kind", draft.Kind.ToString()), ("$date", DateText(draft.Date)),
                ("$description", draft.Description), ("$category", draft.CategoryId), ("$memo", draft.Memo));
            AddMovement(c, tx, key, draft.AccountId, draft.Kind == TransactionKind.Income ? draft.Amount.Centimes : -draft.Amount.Centimes);
            if (draft.DestinationId is not null)
            {
                AddMovement(c, tx, key, draft.DestinationId, draft.Amount.Centimes);
            }

            if (id is not null)
            {
                Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", id));
            }
        });
        return key;
    }

    private static string[] ParseCategoryPath(string? categoryPath)
    {
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            return [];
        }

        var parts = categoryPath.Split('/').Select(part => part.Trim()).ToArray();
        if (parts.Length > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Enter a category or Category / Subcategory with one name on each side of '/'.");
        }

        return parts;
    }

    private static string? ResolveCategoryPath(SqliteConnection c, SqliteTransaction tx, string? transactionId, string[] parts)
    {
        if (parts.Length == 0)
        {
            return null;
        }

        var previousCategory = transactionId is null
            ? null
            : Scalar(c, tx, "SELECT category_id FROM ledger WHERE id=$id", ("$id", transactionId)) as string;
        string? parentId = null;
        var archivedParent = false;
        for (var index = 0; index < parts.Length; index++)
        {
            var id = Scalar(c, tx,
                "SELECT id FROM categories WHERE parent_id IS $parent AND name=$name COLLATE NOCASE",
                ("$parent", parentId), ("$name", parts[index])) as string;
            if (id is null)
            {
                id = Guid.NewGuid().ToString("N");
                Execute(c, tx, "INSERT INTO categories VALUES($id,$name,$parent,0)",
                    ("$id", id), ("$name", parts[index]), ("$parent", parentId));
            }
            else
            {
                var archived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", id))) != 0;
                if (archived && index == 0 && parts.Length == 2)
                {
                    archivedParent = true;
                }
                else if (archived && id != previousCategory)
                {
                    throw new ArgumentException("Restore the archived category before using it for another transaction.");
                }
            }

            parentId = id;
        }

        if (archivedParent && parentId != previousCategory)
        {
            throw new ArgumentException("Restore the archived category before using it for another transaction.");
        }

        return parentId;
    }

    public void DeleteTransaction(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'", id, "Transaction no longer exists.");
        Execute(c, tx, "UPDATE import_sources SET locally_modified=1,transaction_id=NULL WHERE transaction_id=$id", ("$id", id));
        Execute(c, tx, "DELETE FROM ledger WHERE id=$id", ("$id", id));
    });

    public LedgerSnapshot ReadSnapshot()
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = ReadSnapshot(c, tx);
        tx.Commit();
        return result;
    }

    private LedgerSnapshot ReadSnapshot(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = ReadCategories(c, tx);
        var accounts = ReadAccountsWithBalances(c, tx);
        var accountMap = accounts.ToDictionary(a => a.Id);
        var categoryMap = categories.ToDictionary(a => a.Id);
        var entries = new List<LedgerEntry>();
        using (var cmd = Command(c, tx, """
            SELECT l.id,l.kind,l.date,l.description,l.category_id,l.memo,m.account_id,m.amount,d.account_id
            FROM ledger l JOIN movements m ON m.transaction_id=l.id
            LEFT JOIN movements d ON l.kind='Transfer' AND d.transaction_id=l.id AND d.amount>0
            WHERE l.kind<>'OpeningBalance' AND (l.kind<>'Transfer' OR m.amount<0)
            ORDER BY l.date DESC,l.id DESC
            """))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var category = r.IsDBNull(4) ? null : r.GetString(4);
                var account = r.GetString(6);
                var destination = r.IsDBNull(8) ? null : r.GetString(8);
                var draft = new TransactionDraft(Enum.Parse<TransactionKind>(r.GetString(1)), ParseDate(r.GetString(2)), r.GetString(3),
                    new Money(Math.Abs(r.GetInt64(7))), account, destination, category, r.GetString(5));
                entries.Add(new LedgerEntry(r.GetString(0), draft, accountMap[account].Name, destination is null ? null : accountMap[destination].Name,
                    category is null ? null : categoryMap[category].Path));
            }
        }

        var today = Today;
        var monthEntries = entries.Where(e => e.Draft.Date.Year == today.Year && e.Draft.Date.Month == today.Month).ToArray();
        // Validate all months, not only the displayed month, before committing a write.
        foreach (var period in entries.Where(e => e.Draft.Kind != TransactionKind.Transfer).GroupBy(e => (e.Draft.Date.Year, e.Draft.Date.Month, e.Draft.Kind)))
        {
            _ = Total(period.Select(e => e.Draft.Amount.Centimes));
        }

        var top = monthEntries.Where(e => e.Draft.Kind == TransactionKind.Expense)
            .GroupBy(e => e.Draft.CategoryId is null ? "Uncategorized" : categoryMap[e.Draft.CategoryId].ParentId is { } parent ? categoryMap[parent].Name : categoryMap[e.Draft.CategoryId].Name)
            .Select(g => new CategoryTotal(g.Key, Total(g.Select(e => e.Draft.Amount.Centimes))))
            .OrderByDescending(g => g.Amount).Take(5).ToArray();
        return new LedgerSnapshot(accounts, categories, entries, Total(accounts.Select(a => a.Balance.Centimes)),
            Total(monthEntries.Where(e => e.Draft.Kind == TransactionKind.Income).Select(e => e.Draft.Amount.Centimes)),
            Total(monthEntries.Where(e => e.Draft.Kind == TransactionKind.Expense).Select(e => e.Draft.Amount.Centimes)), top,
            Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")));
        Money Total(IEnumerable<long> amounts) => new(checked((long)amounts.Sum(v => (decimal)v)));
    }

    private void Write(Action<SqliteConnection, SqliteTransaction> action)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction();
        action(c, tx);
        ValidateLedgerTotals(c, tx);
        Execute(c, tx, "UPDATE metadata SET revision=revision+1 WHERE id=1");
        tx.Commit();
    }

    private static void ValidateLedgerTotals(SqliteConnection c, SqliteTransaction tx)
    {
        var accountTotals = new Dictionary<string, decimal>();
        using (var cmd = Command(c, tx, "SELECT account_id,amount FROM movements"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetString(0);
                accountTotals[id] = accountTotals.GetValueOrDefault(id) + reader.GetInt64(1);
            }
        }

        foreach (var total in accountTotals.Values)
        {
            _ = checked((long)total);
        }

        _ = checked((long)accountTotals.Values.Sum());
        var monthly = new Dictionary<(string, string), decimal>();
        using (var cmd = Command(c, tx, "SELECT substr(l.date,1,7),l.kind,m.amount FROM ledger l JOIN movements m ON m.transaction_id=l.id WHERE l.kind IN ('Expense','Income')"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(1));
                monthly[key] = monthly.GetValueOrDefault(key) + Math.Abs((decimal)reader.GetInt64(2));
            }
        }

        foreach (var total in monthly.Values)
        {
            _ = checked((long)total);
        }
    }

    private static void ValidateAccount(SqliteConnection c, SqliteTransaction tx, string id, DateOnly date, string? transactionId)
    {
        Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
        var opening = (string)Scalar(c, tx, "SELECT opening_date FROM accounts WHERE id=$id", ("$id", id))!;
        if (date < ParseDate(opening))
        {
            throw new ArgumentException("Transaction date precedes the account's opening date.");
        }

        var archived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM accounts WHERE id=$id", ("$id", id))) != 0;
        if (archived && (transactionId is null || Scalar(c, tx, "SELECT 1 FROM movements WHERE transaction_id=$transaction AND account_id=$account", ("$transaction", transactionId), ("$account", id)) is null))
        {
            throw new ArgumentException("Choose an active account for new movements.");
        }
    }

    private static List<Category> ReadCategories(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = new List<Category>();
        using var cmd = Command(c, tx, "SELECT c.id,c.name,c.parent_id,CASE WHEN p.id IS NULL THEN c.name ELSE p.name || ' / ' || c.name END,c.archived FROM categories c LEFT JOIN categories p ON p.id=c.parent_id ORDER BY 4 COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            categories.Add(new Category(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        }

        return categories;
    }

    private static List<Account> ReadAccountsWithBalances(SqliteConnection c, SqliteTransaction tx)
    {
        var balances = new Dictionary<string, decimal>();
        using (var cmd = Command(c, tx, "SELECT account_id,amount FROM movements"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                balances[r.GetString(0)] = balances.GetValueOrDefault(r.GetString(0)) + r.GetInt64(1);
            }
        }

        var accounts = new List<Account>();
        using (var cmd = Command(c, tx, "SELECT a.id,a.name,a.opening_date,a.archived,m.amount FROM accounts a JOIN movements m ON m.transaction_id='opening:' || a.id AND m.account_id=a.id ORDER BY a.name COLLATE NOCASE"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                accounts.Add(new Account(r.GetString(0), r.GetString(1), ParseDate(r.GetString(2)), new Money(r.GetInt64(4)), r.GetBoolean(3), new Money(checked((long)balances.GetValueOrDefault(r.GetString(0))))));
            }
        }

        return accounts;
    }

    private static void AddMovement(SqliteConnection c, SqliteTransaction tx, string id, string account, long amount) =>
        Execute(c, tx, "INSERT INTO movements VALUES($id,$account,$amount)", ("$id", id), ("$account", account), ("$amount", amount));
    private static void Require(SqliteConnection c, SqliteTransaction tx, string sql, string id, string error)
    {
        if (Scalar(c, tx, sql, ("$id", id)) is null)
        {
            throw new ArgumentException(error);
        }
    }
    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }
    private static object? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }
    private static void Execute(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        cmd.ExecuteNonQuery();
    }
    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly ParseDate(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
