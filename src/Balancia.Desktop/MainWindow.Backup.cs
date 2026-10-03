using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void SaveBackupOnClose()
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        if (snapshotKey is null)
        {
            Log.Information("Shutdown backup skipped; encryption key is unavailable");
            return;
        }

        TrySilently(() =>
        {
            Directory.CreateDirectory(backupPath);
            var fileName = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(backupPath, fileName), snapshotKey);
            var backups = Directory.EnumerateFiles(backupPath, "backup-*.balancia")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            foreach (var oldBackup in backups.Take(Math.Max(0, backups.Length - 10)))
            {
                File.Delete(oldBackup);
            }
            Log.Information("Backup retention applied after successful export, RemovedCount: '{RemovedCount}', RetainedCount: '{RetainedCount}'",
                Math.Max(0, backups.Length - 10), Math.Min(10, backups.Length));
        });
    }

    private void SaveSnapshotOnClose()
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        if (snapshotKey is null)
        {
            Log.Information("Shutdown snapshot skipped; encryption key is unavailable");
            return;
        }

        TrySilently(() =>
        {
            Directory.CreateDirectory(snapshotPath);
            store.ExportSnapshot(Path.Combine(snapshotPath, "Snapshot.balancia"), snapshotKey);
        });
    }

    // Best-effort shutdown writes must never block the application from closing.
    private static void TrySilently(Action action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        using var context = LogContext.PushProperty("ShutdownWriteId", Guid.NewGuid().ToString("N"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Serilog.Log.Information("Starting shutdown write, Operation: '{Operation}'", operation);
            action();
            Log.Information("Finished shutdown write, Operation: '{Operation}', ElapsedMs: '{ElapsedMs}'", operation, elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
        {
            Log.Warning("Shutdown write failed, Operation: '{Operation}', FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                operation, ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
        }
    }
}
