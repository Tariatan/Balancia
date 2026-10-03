using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotImporterTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-import-" + Guid.NewGuid());
    private readonly SnapshotKey key = SnapshotKey.Create("synthetic snapshot test passphrase");

    public SnapshotImporterTests() => Directory.CreateDirectory(dir);

    [Fact]
    public void Import_ValidSnapshot_StagesDatabaseAndReturnsMatchingManifest()
    {
        var sourcePath = Path.Combine(dir, "source.db");
        var source = new LedgerStore(sourcePath);
        source.Initialize();
        source.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(500));
        var archivePath = Path.Combine(dir, "export.balancia");
        var exported = source.ExportSnapshot(archivePath, key);

        var targetPath = Path.Combine(dir, "android.db");
        var (store, manifest) = SnapshotImporter.Import(archivePath, targetPath, key);

        Assert.True(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".staged"));
        Assert.Equal(exported.DatasetId, manifest.DatasetId);
        Assert.Equal(exported.Revision, manifest.Revision);
        var imported = store.ReadSnapshot();
        Assert.Equal(500, imported.Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Import_TargetAlreadyExists_ReplacesPriorCopy()
    {
        var sourcePath = Path.Combine(dir, "source.db");
        var source = new LedgerStore(sourcePath);
        source.Initialize();
        source.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100));
        var firstArchive = Path.Combine(dir, "first.balancia");
        source.ExportSnapshot(firstArchive, key);

        source.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 2), "Gift",
            new Money(50), source.ReadSnapshot().Accounts.Single().Id));
        var secondArchive = Path.Combine(dir, "second.balancia");
        source.ExportSnapshot(secondArchive, key);

        var targetPath = Path.Combine(dir, "android.db");
        SnapshotImporter.Import(firstArchive, targetPath, key);
        var (store, _) = SnapshotImporter.Import(secondArchive, targetPath, key);

        Assert.Equal(150, store.ReadSnapshot().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public void Import_CorruptArchive_ThrowsAndLeavesNoTargetFile()
    {
        var archivePath = Path.Combine(dir, "bad.balancia");
        File.WriteAllText(archivePath, "not a zip archive");
        var targetPath = Path.Combine(dir, "android.db");

        Assert.Throws<InvalidDataException>(() => SnapshotImporter.Import(archivePath, targetPath, key));

        Assert.False(File.Exists(targetPath));
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(dir, true);
    }
}
