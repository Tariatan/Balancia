using Android.Content;
using Serilog;
using Serilog.Debugging;

namespace Balancia.Android;

internal static class ApplicationLogging
{
    internal static void Configure(Context context)
    {
        // App-private, excluded from OS backup, and independent of the snapshot picker.
        SelfLog.Enable(_ => global::Android.Util.Log.Warn("Balancia", "Logging sink failure"));
        try
        {
            var directory = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "log");
            Directory.CreateDirectory(directory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("SourceContext", "Balancia.Android")
                .WriteTo.File(Path.Combine(directory, "balancia-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10,
                    fileSizeLimitBytes: null, rollOnFileSizeLimit: false, buffered: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}][{SourceContext}]{Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();
            Log.Information("Android application starting, Version: '{Version}', ProcessId: '{ProcessId}'",
                typeof(MainApplication).Assembly.GetName().Version, Environment.ProcessId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            global::Android.Util.Log.Warn("Balancia", $"Logging setup failed, FailureType: '{ex.GetType().Name}'");
        }
    }
}
