using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotImporterTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-import-" + Guid.NewGuid());
    private readonly SnapshotKey key = SnapshotKey.Create("synthetic snapshot test passphrase");
    private readonly LedgerStore source;
    private readonly string targetPath;

    public SnapshotImporterTests()
    {
        Directory.CreateDirectory(dir);
        targetPath = Path.Combine(dir, "android.db");
        source = new LedgerStore(Path.Combine(dir, "source.db"));
        source.Initialize();
        source.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(500));
    }

    [Fact]
    public void Import_ValidSnapshot_StagesDatabaseAndReturnsMatchingManifest()
    {
        // Arrange
        var archivePath = Path.Combine(dir, "export.balancia");
        var exported = source.ExportSnapshot(archivePath, key);

        // Act
        var (reader, manifest) = SnapshotImporter.Import(archivePath, targetPath, key);

        // Assert
        Assert.True(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".staged"));
        Assert.Equal(exported.DatasetId, manifest.DatasetId);
        Assert.Equal(exported.Revision, manifest.Revision);
        Assert.Equal(500, reader.ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Import_TargetAlreadyExists_ReplacesPriorCopy()
    {
        // Arrange
        var firstArchive = Path.Combine(dir, "first.balancia");
        source.ExportSnapshot(firstArchive, key);
        source.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 2), "Gift",
            new Money(50), source.ReadSnapshot().Accounts.Single().Id));
        var secondArchive = Path.Combine(dir, "second.balancia");
        source.ExportSnapshot(secondArchive, key);
        SnapshotImporter.Import(firstArchive, targetPath, key);

        // Act
        var (reader, _) = SnapshotImporter.Import(secondArchive, targetPath, key);

        // Assert
        Assert.Equal(550, reader.ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Import_CorruptArchive_ThrowsAndLeavesNoTargetFile()
    {
        // Arrange
        var archivePath = Path.Combine(dir, "bad.balancia");
        File.WriteAllText(archivePath, "not a zip archive");

        // Act
        void Act() => SnapshotImporter.Import(archivePath, targetPath, key);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public void Import_OlderRevisionOfSameDataset_IsRejectedAndKeepsCurrentCopy()
    {
        // Arrange
        var olderArchive = Path.Combine(dir, "older.balancia");
        source.ExportSnapshot(olderArchive, key);
        source.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 2), "Gift",
            new Money(50), source.ReadSnapshot().Accounts.Single().Id));
        var newerArchive = Path.Combine(dir, "newer.balancia");
        source.ExportSnapshot(newerArchive, key);
        SnapshotImporter.Import(newerArchive, targetPath, key);

        // Act
        void Act() => SnapshotImporter.Import(olderArchive, targetPath, key);

        // Assert
        var error = Assert.Throws<InvalidDataException>(Act);
        Assert.Contains("older snapshot", error.Message);
        Assert.Equal(550, LedgerReader.Open(targetPath).ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Import_SameRevisionAgain_IsAccepted()
    {
        // Arrange
        var archivePath = Path.Combine(dir, "same.balancia");
        source.ExportSnapshot(archivePath, key);
        SnapshotImporter.Import(archivePath, targetPath, key);

        // Act
        var (reader, _) = SnapshotImporter.Import(archivePath, targetPath, key);

        // Assert
        Assert.Equal(500, reader.ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_DifferentDataset_IsRejectedAndKeepsCurrentCopy(bool otherHasHigherRevision)
    {
        // Arrange
        var firstArchive = Path.Combine(dir, "first.balancia");
        source.ExportSnapshot(firstArchive, key);
        SnapshotImporter.Import(firstArchive, targetPath, key);
        var other = new LedgerStore(Path.Combine(dir, "other.db"));
        other.Initialize();
        var account = other.SaveAccount(null, "Fresh", new DateOnly(2026, 1, 1), new Money(7));
        if (otherHasHigherRevision)
        {
            other.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 2), "Gift", new Money(1), account));
        }

        var otherArchive = Path.Combine(dir, "other.balancia");
        other.ExportSnapshot(otherArchive, key);

        // Act
        void Act() => SnapshotImporter.Import(otherArchive, targetPath, key);

        // Assert
        var error = Assert.Throws<InvalidDataException>(Act);
        Assert.Contains("different ledger dataset", error.Message);
        Assert.Equal("Cash", LedgerReader.Open(targetPath).ReadSummary().Accounts.Single().Name);
    }

    [Fact]
    public void Import_UnreadableCurrentCopy_IsReplaced()
    {
        // Arrange
        File.WriteAllText(targetPath, "not a database");
        var archivePath = Path.Combine(dir, "export.balancia");
        source.ExportSnapshot(archivePath, key);

        // Act
        var (reader, _) = SnapshotImporter.Import(archivePath, targetPath, key);

        // Assert
        Assert.Equal(500, reader.ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Reader_SummaryAndSearch_ReflectTheImportedLedger()
    {
        // Arrange
        var archivePath = Path.Combine(dir, "export.balancia");
        source.ExportSnapshot(archivePath, key);
        var (reader, _) = SnapshotImporter.Import(archivePath, targetPath, key);

        // Act
        var summary = reader.ReadSummary();
        var page = reader.ReadTransactions(new TransactionsFilter("anything"), 0, 50);

        // Assert
        Assert.Equal(new Money(500), summary.NetWorth);
        Assert.Equal(0, summary.TransactionCount);
        Assert.Equal(summary.Revision, reader.ReadIdentity().Revision);
        Assert.Empty(page.Hits);
    }

    [Fact]
    public void Reader_Database_RejectsWrites()
    {
        // Arrange
        var archivePath = Path.Combine(dir, "export.balancia");
        source.ExportSnapshot(archivePath, key);
        SnapshotImporter.Import(archivePath, targetPath, key);
        using var connection = new SqliteConnectionFactory(targetPath, readOnly: true).Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE metadata SET revision=revision+1";

        // Act
        void Act() => command.ExecuteNonQuery();

        // Assert
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(Act);
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(dir, true);
    }
}
