using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Storage;

/// <summary>Validates and applies a snapshot archive as a fresh local database, for read-only viewers (e.g. Android).</summary>
public static class SnapshotImporter
{
    public static (LedgerReader Reader, SnapshotManifest Manifest) Import(string archivePath, string dbPath, SnapshotKey key)
    {
        var operationId = Guid.NewGuid().ToString("N");
        using var context = LogContext.PushProperty("OperationId", operationId);
        var logger = Log.ForContext(typeof(SnapshotImporter));
        var elapsed = Stopwatch.StartNew();
        var databaseReplaced = false;
        logger.Information("Starting viewer snapshot import, OperationId: '{OperationId}'", operationId);
        var staged = dbPath + ".staged-" + Guid.NewGuid().ToString("N");
        try
        {
            var manifest = LedgerStore.ExtractValidatedSnapshot(archivePath, staged, key);
            logger.Information("Viewer snapshot validated before replacement, OperationId: '{OperationId}'", operationId);
            RejectForeignOrOlder(dbPath, manifest, logger, operationId);
            File.Move(staged, dbPath, true);
            databaseReplaced = true;
            var importedStore = LedgerReader.Open(dbPath);
            logger.Information("Viewer snapshot import completed, OperationId: '{OperationId}', Revision: '{Revision}', ElapsedMs: '{ElapsedMs}'",
                operationId, manifest.Revision, elapsed.ElapsedMilliseconds);
            return (importedStore, manifest);
        }
        catch (Exception ex) when (LedgerStore.IsSnapshotRejection(ex))
        {
            logger.Information("Viewer snapshot import rejected, OperationId: '{OperationId}', FailureType: '{FailureType}', DatabaseReplaced: '{DatabaseReplaced}', ElapsedMs: '{ElapsedMs}'",
                operationId, ex.GetType().Name, databaseReplaced, elapsed.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            logger.Error("Viewer snapshot import failed, OperationId: '{OperationId}', FailureType: '{FailureType}', HResult: '{HResult}', DatabaseReplaced: '{DatabaseReplaced}', ElapsedMs: '{ElapsedMs}'",
                operationId, ex.GetType().Name, ex.HResult, databaseReplaced, elapsed.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            DeleteStaged(staged, logger, operationId);
        }
    }

    /// <summary>Like a Windows restore, refuses a snapshot of another dataset or an earlier revision; an unreadable copy is replaced.</summary>
    private static void RejectForeignOrOlder(string dbPath, SnapshotManifest manifest, ILogger logger, string operationId)
    {
        if (!File.Exists(dbPath))
        {
            return;
        }

        (string DatasetId, long Revision) current;
        try
        {
            current = LedgerReader.Open(dbPath).ReadIdentity();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            logger.Warning("Viewer copy unreadable; replacing it, OperationId: '{OperationId}', FailureType: '{FailureType}'", operationId, ex.GetType().Name);
            return;
        }

        SnapshotAcceptance.Require(manifest, current, "viewer copy");
    }

    /// <summary>Removes the staging file; a cleanup failure is logged and never masks the import outcome.</summary>
    private static void DeleteStaged(string staged, ILogger logger, string operationId)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Viewer snapshot staging cleanup failed, OperationId: '{OperationId}', FailureType: '{FailureType}', HResult: '{HResult}'",
                operationId, ex.GetType().Name, ex.HResult);
        }
    }
}
