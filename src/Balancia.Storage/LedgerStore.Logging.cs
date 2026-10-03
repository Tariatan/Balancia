using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private T LogOperation<T>(Func<T> action, [CallerMemberName] string operation = "")
    {
        var operationId = Guid.NewGuid().ToString("N");
        using var context = LogContext.PushProperty("OperationId", operationId);
        var logger = Log.ForContext<LedgerStore>();
        var elapsed = Stopwatch.StartNew();
        logger.Information("Starting storage operation, Operation: '{Operation}', OperationId: '{OperationId}', DatabasePath: '{DatabasePath}'", operation, operationId, path);
        try
        {
            var result = action();
            logger.Information("Storage operation completed, Operation: '{Operation}', OperationId: '{OperationId}', ElapsedMs: '{ElapsedMs}'",
                operation, operationId, elapsed.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException ||
            exception is InvalidOperationException && operation.StartsWith(nameof(ApplyCsvImport), StringComparison.Ordinal) ||
            (operation is nameof(RestoreSnapshot) or nameof(ValidateSnapshot)) && IsSnapshotRejection(exception))
        {
            logger.Information("Storage operation rejected, Operation: '{Operation}', OperationId: '{OperationId}', FailureType: '{FailureType}'",
                operation, operationId, exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            if (operation is nameof(ExportSnapshot) or nameof(RestoreSnapshot) or nameof(ValidateSnapshot))
            {
                // Snapshot parser/provider messages can contain untrusted data. Log diagnostics only.
                logger.Error("Storage operation failed, Operation: '{Operation}', OperationId: '{OperationId}', ElapsedMs: '{ElapsedMs}', FailureType: '{FailureType}', HResult: '{HResult}'",
                    operation, operationId, elapsed.ElapsedMilliseconds, exception.GetType().Name, exception.HResult);
            }
            else
            {
                logger.Error(exception, "Storage operation failed, Operation: '{Operation}', OperationId: '{OperationId}', ElapsedMs: '{ElapsedMs}'",
                    operation, operationId, elapsed.ElapsedMilliseconds);
            }
            throw;
        }
    }

    internal static bool IsSnapshotRejection(Exception exception) => exception is ArgumentException or InvalidDataException or JsonException ||
        exception is InvalidOperationException and not ObjectDisposedException || exception is SqliteException { SqliteErrorCode: 11 or 26 };
}
