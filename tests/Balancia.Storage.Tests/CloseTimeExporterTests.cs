using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CloseTimeExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-shutdown-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock clock = new(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly LedgerStore store;
    private readonly CloseTimeExporter exporter;
    private readonly SnapshotKey key = SnapshotKey.Create("synthetic close time passphrase");
    private string SnapshotFolder => Path.Combine(directory, "snapshots");
    private string SnapshotFile => Path.Combine(SnapshotFolder, CloseTimeExporter.SnapshotFileName);
    private string BackupFolder => Path.Combine(directory, "backups");

    public CloseTimeExporterTests()
    {
        Directory.CreateDirectory(directory);
        store = new LedgerStore(Path.Combine(directory, "ledger.db"));
        store.Initialize();
        store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100));
        exporter = new CloseTimeExporter(store, clock);
    }

    private void ChangeLedger() => store.SaveAccount(null, "Other", new DateOnly(2026, 1, 1), new Money(5));

    [Fact]
    public void SaveSnapshot_LoadedLedger_WritesAValidSnapshot()
    {
        // Arrange
        exporter.MarkLoaded();

        // Act
        exporter.SaveSnapshot(SnapshotFolder, key);

        // Assert
        Assert.Equal(1L, store.ValidateSnapshot(SnapshotFile, key).Revision);
    }

    [Fact]
    public void SaveSnapshot_LedgerNeverLoaded_KeepsThePreviousSnapshot()
    {
        // Arrange
        exporter.MarkLoaded();
        exporter.SaveSnapshot(SnapshotFolder, key);
        var previous = File.ReadAllBytes(SnapshotFile);
        File.SetLastWriteTimeUtc(SnapshotFile, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        ChangeLedger();
        var neverLoaded = new CloseTimeExporter(store, clock);

        // Act
        neverLoaded.SaveSnapshot(SnapshotFolder, key);

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(SnapshotFile));
    }

    [Fact]
    public void SaveSnapshot_DatabaseRejectedByThisVersion_KeepsThePreviousSnapshot()
    {
        // Arrange
        exporter.MarkLoaded();
        exporter.SaveSnapshot(SnapshotFolder, key);
        var previous = File.ReadAllBytes(SnapshotFile);
        SqliteScript.Execute(Path.Combine(directory, "ledger.db"), "PRAGMA user_version=2");

        // Act
        exporter.SaveSnapshot(SnapshotFolder, key);

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(SnapshotFile));
    }

    [Fact]
    public void SaveSnapshot_WithoutKeyOrFolder_WritesNothing()
    {
        // Arrange
        exporter.MarkLoaded();

        // Act
        exporter.SaveSnapshot(SnapshotFolder, null);
        exporter.SaveSnapshot(null, key);
        exporter.SaveSnapshot("  ", key);

        // Assert
        Assert.False(Directory.Exists(SnapshotFolder));
    }

    [Fact]
    public void SaveBackup_LedgerChangedSinceLoad_WritesABackup()
    {
        // Arrange
        exporter.MarkLoaded();
        ChangeLedger();

        // Act
        exporter.SaveBackup(BackupFolder, key);

        // Assert
        Assert.Single(Directory.GetFiles(BackupFolder, BackupRetention.Pattern));
    }

    [Fact]
    public void SaveBackup_LedgerUnchangedSinceLoad_WritesNothing()
    {
        // Arrange
        exporter.MarkLoaded();

        // Act
        exporter.SaveBackup(BackupFolder, key);

        // Assert
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void SaveBackup_LedgerNeverLoaded_WritesNothingEvenWhenItChanged()
    {
        // Arrange
        ChangeLedger();

        // Act
        exporter.SaveBackup(BackupFolder, key);

        // Assert
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void SaveBackup_WithoutKey_WritesNothing()
    {
        // Arrange
        exporter.MarkLoaded();
        ChangeLedger();

        // Act
        exporter.SaveBackup(BackupFolder, null);

        // Assert
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void MarkLoaded_CalledAgainAfterAChange_StartsANewBaseline()
    {
        // Arrange
        exporter.MarkLoaded();
        ChangeLedger();
        exporter.MarkLoaded();

        // Act
        exporter.SaveBackup(BackupFolder, key);

        // Assert
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void SaveBackup_AfterASuccessfulExport_RemovesBackupsOutsideTheRetentionPolicy()
    {
        // Arrange
        Directory.CreateDirectory(BackupFolder);
        var expired = Path.Combine(BackupFolder, "backup-expired.balancia");
        File.WriteAllText(expired, "x");
        File.SetLastWriteTimeUtc(expired, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        exporter.MarkLoaded();
        ChangeLedger();

        // Act
        exporter.SaveBackup(BackupFolder, key);

        // Assert
        Assert.False(File.Exists(expired));
        Assert.Single(Directory.GetFiles(BackupFolder, BackupRetention.Pattern));
    }

    [Fact]
    public void SaveBackup_FolderCannotBeCreated_DoesNotThrowAndLeavesExistingFilesAlone()
    {
        // Arrange
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");
        exporter.MarkLoaded();
        ChangeLedger();

        // Act
        var error = Record.Exception(() => exporter.SaveBackup(Path.Combine(blocker, "backups"), key));

        // Assert
        Assert.Null(error);
        Assert.Equal("a file where a folder is needed", File.ReadAllText(blocker));
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(directory, true);
    }
}
