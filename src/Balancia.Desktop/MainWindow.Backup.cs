using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void SaveBackupOnClose()
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(backupPath);
            var fileName = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(backupPath, fileName));
            var backups = Directory.EnumerateFiles(backupPath, "backup-*.balancia")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            foreach (var oldBackup in backups.Take(Math.Max(0, backups.Length - 10)))
            {
                File.Delete(oldBackup);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        catch (SqliteException) { }
    }

    private void SaveSnapshotOnClose()
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(snapshotPath);
            store.ExportSnapshot(Path.Combine(snapshotPath, "Snapshot.balancia"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        catch (SqliteException) { }
    }
}
