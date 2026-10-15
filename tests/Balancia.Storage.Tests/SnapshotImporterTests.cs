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
        var (store, manifest) = SnapshotImporter.Import(archivePath, targetPath, key);

        // Assert
        Assert.True(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".staged"));
        Assert.Equal(exported.DatasetId, manifest.DatasetId);
        Assert.Equal(exported.Revision, manifest.Revision);
        Assert.Equal(500, store.ReadSnapshot().Accounts.Single().Balance.Centimes);
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
        var (store, _) = SnapshotImporter.Import(secondArchive, targetPath, key);

        // Assert
        Assert.Equal(550, store.ReadSnapshot().Accounts.Single().Balance.Centimes);
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

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(dir, true);
    }
}
