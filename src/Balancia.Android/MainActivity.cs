using Android.Content;
using Balancia.Storage;
using Serilog;
using Serilog.Context;

namespace Balancia.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public partial class MainActivity : Activity
{
    private const int PickSnapshot = 1001;
    private TextView? status;
    private EditText? search;
    private LedgerReader? viewerReader;
    private bool importing;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildDashboard();
        status!.Text = "No snapshot loaded. Windows is the only writer.";
        var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
        if (File.Exists(dbPath))
        {
            Log.Information("Opening saved Android viewer database");
            try
            {
                viewerReader = LedgerReader.Open(dbPath);
                await DisplaySnapshot("Saved snapshot");
                Log.Information("Saved Android viewer database opened");
            }
            catch (Exception ex)
            {
                Log.Warning("Saved Android viewer database unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                viewerReader = null;
                status.Text = "The saved copy could not be read. Import a snapshot again.";
            }
        }
        else
        {
            Log.Information("Android viewer has no saved snapshot");
        }
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickSnapshot)
        {
            return;
        }

        if (resultCode != Result.Ok || data?.Data is null)
        {
            Log.Information("Android snapshot picker canceled");
            return;
        }

        if (importing)
        {
            Log.Information("Android snapshot selection ignored; import already in progress");
            return;
        }

        importing = true;
        using var context = LogContext.PushProperty("ImportId", Guid.NewGuid().ToString("N"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Log.Information("Starting Android snapshot import");
        var archivePath = Path.Combine(FilesDir!.AbsolutePath, "incoming.balancia");
        try
        {
            status!.Text = "Validating snapshot…";
            await using (var input = ContentResolver!.OpenInputStream(data.Data))
            await using (var output = File.Create(archivePath))
            {
                await input!.CopyToAsync(output);
            }
            Log.Information("Android snapshot copied from document provider");

            await ImportSnapshot(archivePath);
            Log.Information("Android snapshot import interaction finished, ElapsedMs: '{ElapsedMs}'", elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Parser/provider messages may contain document contents or URIs.
            if (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException ||
                ex is InvalidOperationException and not ObjectDisposedException ||
                LedgerFailures.IsUnreadableDatabase(ex))
            {
                Log.Information("Android snapshot import rejected, FailureType: '{FailureType}', ElapsedMs: '{ElapsedMs}'", ex.GetType().Name, elapsed.ElapsedMilliseconds);
            }
            else
            {
                Log.Error("Android snapshot import failed, FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                    ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
            }
            status!.Text = "Snapshot rejected: " + ex.Message;
            new AlertDialog.Builder(this)
                .SetTitle("Snapshot rejected")!
                .SetMessage(ex.Message)!
                .SetPositiveButton("OK", (_, _) => { })!
                .Show();
        }
        finally
        {
            importing = false;
            RefreshDashboard();
            try
            {
                File.Delete(archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Incoming snapshot cleanup failed, FailureType: '{FailureType}'", ex.GetType().Name);
            }
        }
    }

    private void SearchTransactions() => RefreshDashboard();
}
