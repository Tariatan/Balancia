using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int MaxRetainedBackups = 10;

    private void SaveBackupOnClose()
    {
        if (string.IsNullOrWhiteSpace(BackupPath))
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
            Directory.CreateDirectory(BackupPath);
            var fileName = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(BackupPath, fileName), snapshotKey);
            var backups = Directory.EnumerateFiles(BackupPath, "backup-*.balancia")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            var removedCount = Math.Max(0, backups.Length - MaxRetainedBackups);
            foreach (var oldBackup in backups.Take(removedCount))
            {
                File.Delete(oldBackup);
            }
            Log.Information("Backup retention applied after successful export, RemovedCount: '{RemovedCount}', RetainedCount: '{RetainedCount}'",
                removedCount, backups.Length - removedCount);
        });
    }

    private void SaveSnapshotOnClose()
    {
        if (string.IsNullOrWhiteSpace(SnapshotPath))
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
            Directory.CreateDirectory(SnapshotPath);
            store.ExportSnapshot(Path.Combine(SnapshotPath, "Snapshot.balancia"), snapshotKey);
        });
    }

    // Best-effort shutdown writes must never block the application from closing.
    private static void TrySilently(Action action, [CallerMemberName] string operation = "")
    {
        using var context = LogContext.PushProperty("ShutdownWriteId", Guid.NewGuid().ToString("N"));
        var elapsed = Stopwatch.StartNew();
        try
        {
            Log.Information("Starting shutdown write, Operation: '{Operation}'", operation);
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
