using System.Diagnostics;
using System.Runtime.CompilerServices;
using Balancia.Storage;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Desktop;

/// <summary>The best-effort rolling backup and fixed snapshot written when the window closes. A failure is logged and never blocks closing.</summary>
internal sealed class ShutdownExporter(TimeProvider clock)
{
    internal const string SnapshotFileName = "Snapshot.balancia";

    /// <summary>Writes an encrypted backup when the ledger changed since <paramref name="baseline"/>, then applies <see cref="BackupRetention"/>.</summary>
    public void SaveBackup(LedgerStore store, string? folder, SnapshotKey? key, (string DatasetId, long Revision)? baseline)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (key is null)
        {
            Log.Information("Shutdown backup skipped; encryption key is unavailable");
            return;
        }

        TrySilently(() =>
        {
            if (baseline is null || store.ReadLiveMetadata() == baseline)
            {
                Log.Information("Shutdown backup skipped; ledger has not loaded or has not changed");
                return;
            }

            Directory.CreateDirectory(folder);
            var fileName = $"backup-{clock.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(folder, fileName), key);
            var (removed, retained) = BackupRetention.Apply(folder, clock.LocalTimeZone, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
            Log.Information("Backup retention applied after successful export, RemovedCount: '{RemovedCount}', RetainedCount: '{RetainedCount}'",
                removed, retained);
        });
    }

    /// <summary>Overwrites the fixed snapshot file. A ledger that never loaded is skipped so the last usable snapshot survives.</summary>
    public void SaveSnapshot(LedgerStore store, string? folder, SnapshotKey? key, bool ledgerLoaded)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (key is null)
        {
            Log.Information("Shutdown snapshot skipped; encryption key is unavailable");
            return;
        }

        if (!ledgerLoaded)
        {
            Log.Information("Shutdown snapshot skipped; the ledger did not load");
            return;
        }

        TrySilently(() =>
        {
            Directory.CreateDirectory(folder);
            store.ExportSnapshot(Path.Combine(folder, SnapshotFileName), key);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or SqliteException)
        {
            Log.Warning("Shutdown write failed, Operation: '{Operation}', FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                operation, ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
        }
    }
}
