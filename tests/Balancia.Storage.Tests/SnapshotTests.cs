using System.IO.Compression;
using System.Text.Json;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-" + Guid.NewGuid());
    private readonly LedgerStore store;
    public SnapshotTests()
    {
        Directory.CreateDirectory(dir);
        store = new LedgerStore(Path.Combine(dir, "ledger.db"));
        store.Initialize();
        store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100));
    }

    [Fact]
    public void ExportContainsConsistentDatabaseAndManifest()
    {
        var path = Path.Combine(dir, "snapshot.balancia");
        var manifest = store.ExportSnapshot(path);
        Assert.True(File.Exists(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Contains(archive.Entries, e => e.FullName == "ledger.db");
        var json = new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd();
        var read = JsonSerializer.Deserialize<SnapshotManifest>(json)!;
        Assert.Equal(manifest.DatasetId, read.DatasetId);
        Assert.Equal(3, read.SchemaVersion);
        using (var entry = archive.GetEntry("ledger.db")!.Open())
        using (var file = File.Create(Path.Combine(dir, "extracted.db")))
        {
            entry.CopyTo(file);
        }

        using var check = new SqliteConnectionFactory(Path.Combine(dir, "extracted.db")).Open();
        using var command = check.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM accounts";
        Assert.Equal(1L, command.ExecuteScalar());
        Assert.Equal(manifest, store.ValidateSnapshot(path));
    }

    [Fact]
    public void CorruptSnapshotIsRejected()
    {
        var path = Path.Combine(dir, "bad.balancia");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => store.ValidateSnapshot(path));
    }

    [Fact]
    public void OlderSnapshotCannotReplaceCurrentLedger()
    {
        var path = Path.Combine(dir, "older.balancia");
        store.ExportSnapshot(path);
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));
        Assert.Throws<InvalidDataException>(() => store.RestoreSnapshot(path));
        Assert.Single(store.ReadSnapshot().Entries);
    }

    [Fact]
    public void RestoreSnapshotCreatesValidRecoverableBackup()
    {
        var path = Path.Combine(dir, "current.balancia");
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));
        var manifest = store.ExportSnapshot(path);
        var result = store.RestoreSnapshot(path);
        Assert.Equal(manifest.Revision, result.Manifest.Revision);
        Assert.True(File.Exists(result.BackupPath));
        using var backupConnection = new SqliteConnectionFactory(result.BackupPath).Open();
        using var command = backupConnection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", Convert.ToString(command.ExecuteScalar()), ignoreCase: true);
        command.CommandText = "SELECT COUNT(*) FROM ledger WHERE kind<>'OpeningBalance'";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void TopCategoryRollupMatchesBetweenFullAndDesktopSnapshot()
    {
        var parityStore = new LedgerStore(Path.Combine(dir, "parity.db"), new FixedClock());
        parityStore.Initialize();
        var account = parityStore.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100000));
        var foodId = parityStore.SaveCategory(null, "Food", null);
        var lunchId = parityStore.SaveCategory(null, "Lunch", foodId);
        var rentId = parityStore.SaveCategory(null, "Rent", null);
        var today = new DateOnly(2026, 6, 15);
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Groceries", new Money(3000), account, CategoryId: foodId));
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Lunch out", new Money(1500), account, CategoryId: lunchId));
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Rent", new Money(20000), account, CategoryId: rentId));

        var full = parityStore.ReadSnapshot().LargestCategories.OrderBy(c => c.Name).ToArray();
        var desktop = parityStore.ReadDesktopSnapshot().LargestCategories.OrderBy(c => c.Name).ToArray();

        Assert.Equal(full, desktop);
        Assert.Equal(2, full.Length);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            // ignored
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
