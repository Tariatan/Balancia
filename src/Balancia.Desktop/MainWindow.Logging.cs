using Serilog;

namespace Balancia.Desktop;

public partial class MainWindow
{
    internal static void LogWorkflowFailure(Exception exception, string operation)
    {
        var logger = Log.ForContext<MainWindow>();
        if (exception is ArgumentException or FormatException or InvalidDataException ||
            exception is InvalidOperationException && operation == "CSV import")
        {
            logger.Information("Desktop operation rejected, Operation: '{Operation}', FailureType: '{FailureType}'",
                operation, exception.GetType().Name);
            return;
        }

        logger.Error(exception, "Desktop operation failed, Operation: '{Operation}'", operation);
    }
}
