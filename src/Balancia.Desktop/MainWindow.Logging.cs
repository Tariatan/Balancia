using Serilog;

namespace Balancia.Desktop;

public partial class MainWindow
{
    internal static void LogWorkflowFailure(Exception exception, string operation)
    {
        var logger = Log.ForContext<MainWindow>();
        var snapshotOperation = operation is "Snapshot export" or "Snapshot restore";
        if (exception is ArgumentException or FormatException or InvalidDataException ||
            snapshotOperation && (exception is System.Text.Json.JsonException ||
                operation == "Snapshot restore" && exception is InvalidOperationException and not ObjectDisposedException ||
                exception is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 11 or 26 }) ||
            exception is InvalidOperationException && operation == "CSV import")
        {
            logger.Information("Desktop operation rejected, Operation: '{Operation}', FailureType: '{FailureType}'",
                operation, exception.GetType().Name);
            return;
        }

        if (snapshotOperation)
        {
            logger.Error("Desktop operation failed, Operation: '{Operation}', FailureType: '{FailureType}', HResult: '{HResult}'",
                operation, exception.GetType().Name, exception.HResult);
        }
        else
        {
            logger.Error(exception, "Desktop operation failed, Operation: '{Operation}'", operation);
        }
    }
}
