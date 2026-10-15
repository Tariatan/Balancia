using System.IO.Compression;
using System.Text;
using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-" + Guid.NewGuid());
    private readonly LedgerStore store;
    private readonly SnapshotKey key = SnapshotKey.Create("synthetic snapshot test passphrase");
    private readonly string account;

    public SnapshotTests()
    {
        Directory.CreateDirectory(dir);
        store = new LedgerStore(Path.Combine(dir, "ledger.db"));
        store.Initialize();
        account = store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100));
    }

    [Fact]
    public void ExportSnapshot_ValidLedger_WritesEncryptedEnvelopeInsteadOfZip()
    {
        // Arrange
        var path = Path.Combine(dir, "snapshot.balancia");

        // Act
        store.ExportSnapshot(path, key);

        // Assert
        Assert.Equal("BALENC01", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 8));
        Assert.Throws<InvalidDataException>(() => ZipFile.OpenRead(path));
    }

    [Fact]
    public void Import_ExportedSnapshot_ExtractsConsistentDatabaseAndManifest()
    {
        // Arrange
        var path = Path.Combine(dir, "snapshot.balancia");
        var extractedPath = Path.Combine(dir, "extracted.db");
        var manifest = store.ExportSnapshot(path, key);

        // Act
        var (_, read) = SnapshotImporter.Import(path, extractedPath, key);

        // Assert
        Assert.Equal(manifest, read);
        Assert.Equal(1L, SqliteScript.Scalar(extractedPath, "SELECT COUNT(*) FROM accounts"));
    }

    [Fact]
    public void ValidateSnapshot_ExportedSnapshot_ReturnsExportedManifest()
    {
        // Arrange
        var path = Path.Combine(dir, "snapshot.balancia");
        var manifest = store.ExportSnapshot(path, key);

        // Act
        var validated = store.ValidateSnapshot(path, key);

        // Assert
        Assert.Equal(manifest, validated);
    }

    [Fact]
    public void ValidateSnapshot_CorruptFile_ThrowsInvalidDataException()
    {
        // Arrange
        var path = Path.Combine(dir, "bad.balancia");
        File.WriteAllText(path, "not a zip");

        // Act
        void Act() => store.ValidateSnapshot(path, key);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void RestoreSnapshot_RegularCategories_DerivesUsageFromRestoredTransactions()
    {
        // Arrange
        var parent = store.SaveCategory(null, "Shop", null);
        var category = store.SaveCategory(null, "Misc", parent);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, DateOnly.FromDateTime(DateTime.Today),
            "Synthetic purchase", new Money(1), account, CategoryId: category));
        var snapshotPath = Path.Combine(dir, "selections.balancia");
        store.ExportSnapshot(snapshotPath, key);

        // Act
        store.RestoreSnapshot(snapshotPath, key);

        // Assert
        Assert.Equal(["Shop / Misc"], store.ReadRegularCategoryPaths());
    }

    [Fact]
    public void RestoreSnapshot_OlderThanCurrentLedger_ThrowsAndKeepsCurrentEntries()
    {
        // Arrange
        var path = Path.Combine(dir, "older.balancia");
        store.ExportSnapshot(path, key);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));

        // Act
        void Act() => store.RestoreSnapshot(path, key);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
        Assert.Single(store.ReadSnapshot().Entries);
    }

    [Fact]
    public void RestoreSnapshot_CurrentSnapshot_ReturnsManifestAndCreatesValidBackup()
    {
        // Arrange
        var path = Path.Combine(dir, "current.balancia");
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Coffee", new Money(1), account));
        var manifest = store.ExportSnapshot(path, key);

        // Act
        var result = store.RestoreSnapshot(path, key);

        // Assert
        Assert.Equal(manifest.Revision, result.Manifest.Revision);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("ok", Convert.ToString(SqliteScript.Scalar(result.BackupPath, "PRAGMA integrity_check")), ignoreCase: true);
        Assert.Equal(1L, SqliteScript.Scalar(result.BackupPath, "SELECT COUNT(*) FROM ledger WHERE kind<>'OpeningBalance'"));
    }

    [Fact]
    public void LargestCategories_ParentAndChildExpenses_MatchBetweenFullAndDesktopSnapshot()
    {
        // Arrange
        var parityStore = new LedgerStore(Path.Combine(dir, "parity.db"),
            new TestClock(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)));
        parityStore.Initialize();
        var parityAccount = parityStore.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100000));
        var foodId = parityStore.SaveCategory(null, "Food", null);
        var lunchId = parityStore.SaveCategory(null, "Lunch", foodId);
        var rentId = parityStore.SaveCategory(null, "Rent", null);
        var today = new DateOnly(2026, 6, 15);
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Groceries", new Money(3000), parityAccount, CategoryId: foodId));
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Lunch out", new Money(1500), parityAccount, CategoryId: lunchId));
        parityStore.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, today, "Rent", new Money(20000), parityAccount, CategoryId: rentId));

        // Act
        var full = parityStore.ReadSnapshot().LargestCategories.OrderBy(c => c.Name).ToArray();
        var desktop = parityStore.ReadDesktopSnapshot().LargestCategories.OrderBy(c => c.Name).ToArray();

        // Assert
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
}
