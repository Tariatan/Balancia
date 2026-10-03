using _Microsoft.Android.Resource.Designer;
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
    private TextView? results;
    private LedgerStore? viewerStore;
    private bool importing;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(ResourceConstant.Layout.activity_main);
        status = FindViewById<TextView>(ResourceConstant.Id.Status);
        search = FindViewById<EditText>(ResourceConstant.Id.Search);
        results = FindViewById<TextView>(ResourceConstant.Id.Results);
        FindViewById<Button>(ResourceConstant.Id.SelectSnapshot)!.Click += (_, _) =>
        {
            Log.Information("Android snapshot picker opened");
            // Custom .balancia extensions have no standard Android MIME type. Let
            // the provider show documents, then enforce the Balancia archive
            // format after selection.
            var picker = new Intent(Intent.ActionOpenDocument)
                .SetType("*/*")
                .AddCategory(Intent.CategoryOpenable);
            StartActivityForResult(picker, PickSnapshot);
        };
        FindViewById<Button>(ResourceConstant.Id.SearchButton)!.Click += (_, _) => SearchTransactions();
        status!.Text = "No snapshot loaded. Windows is the only writer.";
        var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
        if (File.Exists(dbPath))
        {
            Log.Information("Opening saved Android viewer database");
            try
            {
                viewerStore = new LedgerStore(dbPath);
                await DisplaySnapshot("Saved snapshot");
                Log.Information("Saved Android viewer database opened");
            }
            catch (Exception ex)
            {
                Log.Warning("Saved Android viewer database unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                viewerStore = null;
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
            results!.Text = "";
            Log.Information("Android snapshot import interaction finished, ElapsedMs: '{ElapsedMs}'", elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Parser/provider messages may contain document contents or URIs.
            if (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException ||
                ex is InvalidOperationException and not ObjectDisposedException ||
                ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 11 or 26 })
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
            try
            {
                File.Delete(archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Serilog.Log.Warning("Incoming snapshot cleanup failed, FailureType: '{FailureType}'", ex.GetType().Name);
            }
        }
    }

    private void SearchTransactions()
    {
        if (importing)
        {
            return;
        }

        if (viewerStore is null)
        {
            Log.Information("Android search skipped; no snapshot loaded");
            results!.Text = "Load a snapshot first.";
            return;
        }
        try
        {
            Log.Information("Starting Android transaction search");
            var page = viewerStore.ReadTransactions(new TransactionsFilter(search?.Text), 0, 50);
            results!.Text = page.Hits.Count == 0 ? "No matching transactions." :
                string.Join("\n", page.Hits.Select(h => $"{h.Entry.Draft.Date:yyyy-MM-dd} · {h.Entry.Draft.Kind} · {h.Entry.Draft.Amount.Francs:N2} · {h.Entry.Draft.Description}"));
            Log.Information("Android transaction search completed, ResultCount: '{ResultCount}'", page.Hits.Count);
        }
        catch (Exception ex)
        {
            Log.Error("Android transaction search failed, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            results!.Text = "Search failed: " + ex.Message;
        }
    }
}
