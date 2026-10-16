namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly ShutdownExporter shutdownExporter;

    // Set once the ledger initialized; a changed baseline later marks the session as having modified the ledger.
    private (string DatasetId, long Revision)? backupBaseline;

    private void SaveBackupOnClose() => shutdownExporter.SaveBackup(store, BackupPath, keys.Key, backupBaseline);

    private void SaveSnapshotOnClose() => shutdownExporter.SaveSnapshot(store, SnapshotPath, keys.Key, ledgerLoaded: backupBaseline is not null);
}
