using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int MaxRetainedBackups = 21;
    private const int MaxDailyBackups = 3;
    private (string DatasetId, long Revision)? backupBaseline;

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
            if (backupBaseline is null || store.ReadLiveMetadata() == backupBaseline)
            {
                Log.Information("Shutdown backup skipped; ledger has not changed");
                return;
            }

            Directory.CreateDirectory(BackupPath);
            var fileName = $"backup-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(BackupPath, fileName), snapshotKey);
            var backups = Directory.EnumerateFiles(BackupPath, "backup-*.balancia")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            var retained = backups
                .GroupBy(file => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(file.LastWriteTimeUtc, timeProvider.LocalTimeZone)))
                .SelectMany(day => day.Take(MaxDailyBackups))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .Take(MaxRetainedBackups)
                .ToHashSet();
            var removedCount = backups.Length - retained.Count;
            foreach (var oldBackup in backups.Where(file => !retained.Contains(file)))
            {
                oldBackup.Delete();
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
