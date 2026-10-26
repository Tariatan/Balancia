using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Context;

namespace Balancia.Storage;

/// <summary>
/// The best-effort rolling backup and fixed snapshot written when a session on one ledger ends. A failure is logged and never thrown,
/// so closing cannot be blocked. Call <see cref="MarkLoaded"/> once the ledger initialized: a ledger that never loaded writes nothing,
/// so the last usable files survive, and the revision at that moment is the baseline that tells whether the session changed anything.
/// </summary>
public sealed class CloseTimeExporter(LedgerStore store, TimeProvider? clock = null)
{
    public const string SnapshotFileName = "Snapshot.balancia";

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private (string DatasetId, long Revision)? baseline;

    // Resolved per call, so it follows the logger the application configures at startup.
    private static ILogger Logger => Log.ForContext<CloseTimeExporter>();

    /// <summary>Records the ledger as loaded and its current revision as the baseline; calling it again starts a new baseline.</summary>
    public void MarkLoaded() => baseline = store.ReadLiveMetadata();

    /// <summary>Writes an encrypted backup when the ledger changed since <see cref="MarkLoaded"/>, then applies <see cref="BackupRetention"/>.</summary>
    public void SaveBackup(string? folder, SnapshotKey? key)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (key is null)
        {
            Logger.Information("Shutdown backup skipped; encryption key is unavailable");
            return;
        }

        TrySilently(() =>
        {
            if (baseline is null || store.ReadLiveMetadata() == baseline)
            {
                Logger.Information("Shutdown backup skipped; ledger has not loaded or has not changed");
                return;
            }

            Directory.CreateDirectory(folder);
            var fileName = $"backup-{clock.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(folder, fileName), key);
            var (removed, retained) = BackupRetention.Apply(folder, clock.LocalTimeZone, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
            Logger.Information("Backup retention applied after successful export, RemovedCount: '{RemovedCount}', RetainedCount: '{RetainedCount}'",
                removed, retained);
        });
    }

    /// <summary>Overwrites the fixed snapshot file. A ledger that never loaded is skipped so the last usable snapshot survives.</summary>
    public void SaveSnapshot(string? folder, SnapshotKey? key)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (key is null)
        {
            Logger.Information("Shutdown snapshot skipped; encryption key is unavailable");
            return;
        }

        if (baseline is null)
        {
            Logger.Information("Shutdown snapshot skipped; the ledger did not load");
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
            Logger.Information("Starting shutdown write, Operation: '{Operation}'", operation);
            action();
            Logger.Information("Finished shutdown write, Operation: '{Operation}', ElapsedMs: '{ElapsedMs}'", operation, elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException
            || LedgerFailures.IsDatabaseFailure(ex))
        {
            Logger.Warning("Shutdown write failed, Operation: '{Operation}', FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                operation, ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
        }
    }
}
