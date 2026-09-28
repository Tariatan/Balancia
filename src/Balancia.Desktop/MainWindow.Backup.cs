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

        TrySilently(() =>
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
        });
    }

    private void SaveSnapshotOnClose()
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        TrySilently(() =>
        {
            Directory.CreateDirectory(snapshotPath);
            store.ExportSnapshot(Path.Combine(snapshotPath, "Snapshot.balancia"));
        });
    }

    // Best-effort shutdown writes must never block the application from closing.
    private static void TrySilently(Action action)
    {
        try
        {
            action();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        catch (SqliteException) { }
    }
}
