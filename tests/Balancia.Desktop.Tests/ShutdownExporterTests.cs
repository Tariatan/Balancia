using Balancia.Core;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ShutdownExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-shutdown-" + Guid.NewGuid().ToString("N"));
    private readonly LedgerStore store;
    private readonly ShutdownExporter exporter = new(new ManualClock(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)));
    private readonly SnapshotKey key = DesktopWorkspace.CreateKey();
    private string SnapshotFolder => Path.Combine(directory, "snapshots");
    private string SnapshotFile => Path.Combine(SnapshotFolder, ShutdownExporter.SnapshotFileName);

    public ShutdownExporterTests()
    {
        Directory.CreateDirectory(directory);
        store = new LedgerStore(Path.Combine(directory, "ledger.db"));
        store.Initialize();
        store.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(100));
    }

    [Fact]
    public void SaveSnapshot_LoadedLedger_WritesAValidSnapshot()
    {
        // Arrange
        // The fixture ledger is initialized.

        // Act
        exporter.SaveSnapshot(store, SnapshotFolder, key, ledgerLoaded: true);

        // Assert
        Assert.Equal(1L, store.ValidateSnapshot(SnapshotFile, DesktopWorkspace.CreateKey()).Revision);
    }

    [Fact]
    public void SaveSnapshot_LedgerNeverLoaded_KeepsThePreviousSnapshot()
    {
        // Arrange
        exporter.SaveSnapshot(store, SnapshotFolder, key, ledgerLoaded: true);
        var previous = File.ReadAllBytes(SnapshotFile);
        File.SetLastWriteTimeUtc(SnapshotFile, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        store.SaveAccount(null, "Other", new DateOnly(2026, 1, 1), new Money(5));

        // Act
        exporter.SaveSnapshot(store, SnapshotFolder, key, ledgerLoaded: false);

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(SnapshotFile));
    }

    [Fact]
    public void SaveSnapshot_DatabaseRejectedByThisVersion_KeepsThePreviousSnapshot()
    {
        // Arrange
        exporter.SaveSnapshot(store, SnapshotFolder, key, ledgerLoaded: true);
        var previous = File.ReadAllBytes(SnapshotFile);
        using (var connection = new SqliteConnectionFactory(Path.Combine(directory, "ledger.db")).Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=2";
            command.ExecuteNonQuery();
        }

        // Act
        exporter.SaveSnapshot(store, SnapshotFolder, key, ledgerLoaded: true);

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(SnapshotFile));
    }

    [Fact]
    public void SaveSnapshot_WithoutKeyOrFolder_WritesNothing()
    {
        // Arrange
        // No key, then no folder.

        // Act
        exporter.SaveSnapshot(store, SnapshotFolder, null, ledgerLoaded: true);
        exporter.SaveSnapshot(store, null, key, ledgerLoaded: true);
        exporter.SaveSnapshot(store, "  ", key, ledgerLoaded: true);

        // Assert
        Assert.False(Directory.Exists(SnapshotFolder));
    }

    [Fact]
    public void SaveBackup_LedgerChangedSinceBaseline_WritesABackup()
    {
        // Arrange
        var baseline = store.ReadLiveMetadata();
        store.SaveAccount(null, "Other", new DateOnly(2026, 1, 1), new Money(5));
        var folder = Path.Combine(directory, "backups");

        // Act
        exporter.SaveBackup(store, folder, key, baseline);

        // Assert
        Assert.Single(Directory.GetFiles(folder, BackupRetention.Pattern));
    }

    [Fact]
    public void SaveBackup_LedgerUnchangedOrNeverLoaded_WritesNothing()
    {
        // Arrange
        var folder = Path.Combine(directory, "backups");

        // Act
        exporter.SaveBackup(store, folder, key, store.ReadLiveMetadata());
        exporter.SaveBackup(store, folder, key, baseline: null);

        // Assert
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void SaveBackup_WithoutKey_WritesNothing()
    {
        // Arrange
        var folder = Path.Combine(directory, "backups");

        // Act
        exporter.SaveBackup(store, folder, null, baseline: ("other", 0));

        // Assert
        Assert.False(Directory.Exists(folder));
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(directory, true);
    }
}
