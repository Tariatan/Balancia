using System.Diagnostics;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Balancia.Desktop;

internal static class ApplicationLogging
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}][{SourceContext}]{Message:lj} {Properties:j}{NewLine}{Exception}";

    public static void Configure() => Configure(ApplicationPaths.LogDirectory);

    internal static void Configure(string logDirectory)
    {
        SelfLog.Enable(message => Trace.TraceError("Serilog: {0}", message));
        try
        {
            Directory.CreateDirectory(logDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("SourceContext", "Balancia.Desktop")
                .WriteTo.File(
                    Path.Combine(logDirectory, "balancia-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 180,
                    fileSizeLimitBytes: null,
                    rollOnFileSizeLimit: false,
                    shared: true,
                    outputTemplate: OutputTemplate)
                .CreateLogger();
            Log.Information("Application starting, Version: '{Version}', ProcessId: '{ProcessId}', LogDirectory: '{LogDirectory}'",
                typeof(Program).Assembly.GetName().Version, Environment.ProcessId, logDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError("Unable to initialize Balancia logging: {0}", exception);
        }
    }
}
