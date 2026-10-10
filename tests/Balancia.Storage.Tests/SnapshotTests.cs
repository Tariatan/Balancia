using System.IO.Compression;
using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-" + Guid.NewGuid());
    private readonly LedgerStore store;
    private readonly SnapshotKey key = SnapshotKey.Create("synthetic snapshot test passphrase");
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
        var manifest = store.ExportSnapshot(path, key);
        Assert.True(File.Exists(path));
        Assert.Equal("BALENC01", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 8));
        Assert.Throws<InvalidDataException>(() => ZipFile.OpenRead(path));
        var (_, read) = SnapshotImporter.Import(path, Path.Combine(dir, "extracted.db"), key);
        Assert.Equal(manifest, read);

        using var check = new SqliteConnectionFactory(Path.Combine(dir, "extracted.db")).Open();
        using var command = check.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM accounts";
        Assert.Equal(1L, command.ExecuteScalar());
        Assert.Equal(manifest, store.ValidateSnapshot(path, key));
    }

    [Fact]
    public void CorruptSnapshotIsRejected()
    {
        var path = Path.Combine(dir, "bad.balancia");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => store.ValidateSnapshot(path, key));
    }

    [Fact]
    public void RestoreSnapshot_CategorySelections_PreservesHistory()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Shop", null);
        var category = store.SaveCategory(null, "Misc", parent);
        store.RememberCategorySelection(category);
        var snapshotPath = Path.Combine(dir, "selections.balancia");
        store.ExportSnapshot(snapshotPath, key);

        // Act
        store.RestoreSnapshot(snapshotPath, key);

        // Assert
        Assert.Equal(["Shop / Misc"], store.ReadRecentCategoryPaths());
    }

    [Fact]
    public void RestoreSnapshot_LegacySchemaThreeWithoutHistory_CreatesEmptyHistory()
    {
        // Arrange
        var databasePath = Path.Combine(dir, "ledger.db");
        using (var connection = new SqliteConnectionFactory(databasePath).Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE category_selections";
            command.ExecuteNonQuery();
        }
        var snapshotPath = Path.Combine(dir, "legacy.balancia");
        var manifest = store.ExportSnapshot(snapshotPath, key);
        store.Initialize();

        // Act
        store.RestoreSnapshot(snapshotPath, key);

        // Assert
        Assert.Empty(store.ReadRecentCategoryPaths());
        Assert.Equal(manifest.Revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void OlderSnapshotCannotReplaceCurrentLedger()
    {
        var path = Path.Combine(dir, "older.balancia");
        store.ExportSnapshot(path, key);
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));
        Assert.Throws<InvalidDataException>(() => store.RestoreSnapshot(path, key));
        Assert.Single(store.ReadSnapshot().Entries);
    }

    [Fact]
    public void RestoreSnapshotCreatesValidRecoverableBackup()
    {
        var path = Path.Combine(dir, "current.balancia");
        var account = store.ReadSnapshot().Accounts.Single().Id;
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));
        var manifest = store.ExportSnapshot(path, key);
        var result = store.RestoreSnapshot(path, key);
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
        key.Dispose();
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
