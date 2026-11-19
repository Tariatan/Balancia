using Android.Content.PM;
using Android.Views;
using Serilog;
using AndroidManifest = Android.Manifest;

namespace Balancia.Android;

public partial class MainActivity
{
    private const int ExportLogsPermission = 1002;
    private bool exportingLogs;

    private async void OnTitleLongClick(object? sender, View.LongClickEventArgs args)
    {
        args.Handled = true;
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) &&
            CheckSelfPermission(AndroidManifest.Permission.WriteExternalStorage) != Permission.Granted)
        {
            RequestPermissions([AndroidManifest.Permission.WriteExternalStorage], ExportLogsPermission);
            return;
        }

        await ExportLogsAsync(CancellationToken.None);
    }

    public override async void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != ExportLogsPermission)
        {
            return;
        }

        if (grantResults is [Permission.Granted])
        {
            await ExportLogsAsync(CancellationToken.None);
        }
        else
        {
            Toast.MakeText(this, "Allow storage access to export logs to Download.", ToastLength.Long)?.Show();
        }
    }

    private async Task ExportLogsAsync(CancellationToken cancellationToken)
    {
        if (exportingLogs)
        {
            return;
        }

        exportingLogs = true;
        string message;
        try
        {
            var context = ApplicationContext ?? this;
            var fileName = await Task.Run(() => LogArchiveExporter.Export(context), cancellationToken);
            message = fileName is null ? "No Balancia logs to export." : $"Saved to Download: {fileName}";
        }
        catch (Exception ex)
        {
            Log.Error("Android log export failed, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            message = "Could not export Balancia logs to Download.";
        }
        finally
        {
            exportingLogs = false;
        }

        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }
}
