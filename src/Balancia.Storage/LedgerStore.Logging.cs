using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private T LogOperation<T>(Func<T> action, [CallerMemberName] string operation = "")
    {
        var operationId = Guid.NewGuid().ToString("N");
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
            exception is InvalidOperationException && operation.StartsWith(nameof(ApplyCsvImport), StringComparison.Ordinal))
        {
            logger.Information("Storage operation rejected, Operation: '{Operation}', OperationId: '{OperationId}', FailureType: '{FailureType}'",
                operation, operationId, exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Storage operation failed, Operation: '{Operation}', OperationId: '{OperationId}', ElapsedMs: '{ElapsedMs}'",
                operation, operationId, elapsed.ElapsedMilliseconds);
            throw;
        }
    }
}
