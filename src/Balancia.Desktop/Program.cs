using Avalonia;
using Avalonia.Threading;
using Serilog;

namespace Balancia.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationLogging.Configure();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled application exception, Terminating: '{Terminating}'", eventArgs.IsTerminating);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            Log.Error(eventArgs.Exception, "Unobserved background task exception");
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
            Log.Fatal(eventArgs.Exception, "Unhandled UI exception");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.Information("Application stopped");
            Log.CloseAndFlush();
        }
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
