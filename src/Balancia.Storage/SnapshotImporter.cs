using System.Diagnostics;
using Serilog;
using Serilog.Context;

namespace Balancia.Storage;

/// <summary>Validates and applies a snapshot archive as a fresh local database, for read-only viewers (e.g. Android).</summary>
public static class SnapshotImporter
{
    public static (LedgerStore Store, SnapshotManifest Manifest) Import(string archivePath, string dbPath, SnapshotKey? key = null)
    {
        var operationId = Guid.NewGuid().ToString("N");
        using var context = LogContext.PushProperty("OperationId", operationId);
        var logger = Log.ForContext(typeof(SnapshotImporter)).ForContext("OperationId", operationId);
        var elapsed = Stopwatch.StartNew();
        var databaseReplaced = false;
        logger.Information("Starting viewer snapshot import, OperationId: '{OperationId}'", operationId);
        var staged = dbPath + ".staged-" + Guid.NewGuid().ToString("N");
        try
        {
            var manifest = LedgerStore.ExtractValidatedSnapshot(archivePath, staged, key);
            logger.Information("Viewer snapshot validated before replacement, OperationId: '{OperationId}'", operationId);
            File.Move(staged, dbPath, true);
            databaseReplaced = true;
            var importedStore = new LedgerStore(dbPath);
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
            if (File.Exists(staged))
            {
                try
                {
                    File.Delete(staged);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.Warning("Viewer snapshot staging cleanup failed, OperationId: '{OperationId}', FailureType: '{FailureType}', HResult: '{HResult}'",
                        operationId, ex.GetType().Name, ex.HResult);
                    throw;
                }
            }
        }
    }
}
