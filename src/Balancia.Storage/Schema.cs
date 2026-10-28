using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    internal const int SchemaVersion = 3;

    public void Initialize() => LogOperation(InitializeCore);

    private void InitializeCore()
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction();
        var version = Convert.ToInt64(LedgerSql.Scalar(c, tx, "PRAGMA user_version"));
        if (version == 0)
        {
            if (Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0)
            {
                throw new InvalidOperationException("The selected file is not an empty Balancia database.");
            }

            CreateTables(c, tx);
        }
        else if (version != SchemaVersion)
        {
            throw new InvalidOperationException("This database version is not supported. Use a compatible Balancia version.");
        }

        tx.Commit();
        Serilog.Log.ForContext<LedgerStore>().Information("Ledger schema ready, SchemaVersion: '{SchemaVersion}'", SchemaVersion);
    }

    // import_sources is no longer written or read; it stays in the schema because dropping it needs a new schema version, and with it a
    // migration of databases, snapshots and backups.
    private static void CreateTables(SqliteConnection c, SqliteTransaction tx) => LedgerSql.Execute(c, tx, $$"""
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
        CREATE TABLE import_sources (
            source TEXT NOT NULL, external_id TEXT NOT NULL,
            transaction_id TEXT REFERENCES ledger(id) ON DELETE SET NULL,
            fingerprint TEXT NOT NULL, raw_rows_json TEXT NOT NULL,
            locally_modified INTEGER NOT NULL DEFAULT 0 CHECK(locally_modified IN (0,1)),
            PRIMARY KEY(source,external_id));
        CREATE UNIQUE INDEX import_transaction ON import_sources(transaction_id);
        CREATE TABLE reminder_templates (
            id TEXT PRIMARY KEY, description TEXT NOT NULL CHECK(length(trim(description))>0),
            expected_date TEXT NOT NULL, indicative_amount INTEGER NOT NULL CHECK(indicative_amount>0),
            interval_months INTEGER NOT NULL CHECK(interval_months>0), archived INTEGER NOT NULL CHECK(archived IN (0,1)));
        INSERT INTO metadata VALUES(1, $dataset, 0);
        PRAGMA user_version={{SchemaVersion}};
        """, ("$dataset", Guid.NewGuid().ToString("N")));
}
