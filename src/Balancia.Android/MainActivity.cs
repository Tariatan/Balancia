using Android.Content;
using Android.Provider;
using Balancia.Storage;
using Serilog;
using Serilog.Context;

namespace Balancia.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public partial class MainActivity : Activity
{
    private const int PickSnapshotFolderRequest = 1001;
    private TextView? status;
    private EditText? search;
    private LedgerReader? viewerReader;
    private bool importing;
    private string? snapshotFolder;
    private const string SnapshotFolderPreference = "snapshot-folder";

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        using var preferences = GetSharedPreferences("viewer", FileCreationMode.Private)!;
        lightTheme = preferences.GetBoolean("light-theme", false);
        ApplyTheme();
        base.OnCreate(savedInstanceState);
        snapshotFolder = preferences.GetString(SnapshotFolderPreference, null);
        driveSnapshotId = preferences.GetString(DriveSnapshotPreference, null);
        BuildDashboard();
        status!.Text = "No snapshot loaded";
        var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
        if (File.Exists(dbPath))
        {
            try
            {
                viewerReader = LedgerReader.Open(dbPath);
                await DisplaySnapshot();
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
        if (requestCode == DriveAuthorizationRequest)
        {
            CompleteDriveAuthorization(resultCode, data);
            return;
        }

        if (requestCode != PickSnapshotFolderRequest)
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

        await LoadSnapshot(data.Data, rememberSource: true);
    }

    private async void RefreshSnapshot()
    {
        if (importing) { return; }
        if (driveSnapshotId is not null)
        {
            await LoadSnapshot(null, rememberSource: false);
            return;
        }
        if (snapshotFolder is null)
        {
            PickSnapshotFolder();
            return;
        }
        using var folder = global::Android.Net.Uri.Parse(snapshotFolder)!;
        await LoadSnapshot(folder, rememberSource: false);
    }

    private async Task LoadSnapshot(global::Android.Net.Uri? source, bool rememberSource)
    {
        if (importing) { return; }
        var useDrive = source is null || source.Authority is "com.google.android.apps.docs.storage" or "com.google.android.apps.docs.storage.legacy";
        importing = true;
        using var context = LogContext.PushProperty("ImportId", Guid.NewGuid().ToString("N"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var archivePath = Path.Combine(FilesDir!.AbsolutePath, "incoming.balancia");
        var previousStatus = status!.Text;
        try
        {
            status!.Text = "Loading snapshot…";
            string? importedDriveId = null;
            if (useDrive)
            {
                importedDriveId = await DownloadDriveSnapshotAsync(rememberSource ? null : driveSnapshotId, archivePath, CancellationToken.None);
            }
            else if (source is not null)
            {
                await Task.Run(() => CopyProviderSnapshotAsync(source, archivePath, CancellationToken.None));
            }
            status.Text = "Validating snapshot…";
            var outcome = await ImportSnapshot(archivePath, keepCurrentRevision: true);

            if (outcome == ViewerImportOutcome.AlreadyCurrent)
            {
                status!.Text = previousStatus;
                Toast.MakeText(this, "No newer snapshot available.", ToastLength.Short)?.Show();
            }

            if (importedDriveId is not null && outcome != ViewerImportOutcome.Canceled)
            {
                driveSnapshotId = importedDriveId;
                SaveDriveSnapshotSelection(importedDriveId);
            }
            else if (source is not null && rememberSource && outcome != ViewerImportOutcome.Canceled)
            {
                snapshotFolder = source.ToString();
                try
                {
                    ContentResolver!.TakePersistableUriPermission(source, ActivityFlags.GrantReadUriPermission);
                    using var preferences = GetSharedPreferences("viewer", FileCreationMode.Private)!;
                    using var editor = preferences.Edit()!;
                    editor.PutString(SnapshotFolderPreference, snapshotFolder);
                    editor.Remove(DriveSnapshotPreference);
                    driveSnapshotId = null;
                    if (!editor.Commit())
                    {
                        Log.Warning("Snapshot source preference could not be saved");
                        Toast.MakeText(this, "Snapshot loaded. Choose the source again after restarting the app.", ToastLength.Long)!.Show();
                    }
                }
                catch (Java.Lang.SecurityException ex)
                {
                    Log.Warning("Snapshot source access could not be remembered, FailureType: '{FailureType}'", ex.GetType().Name);
                    Toast.MakeText(this, "Snapshot loaded. This provider requires choosing the source again after restarting the app.", ToastLength.Long)!.Show();
                }
            }
            Log.Information("Android snapshot import finished, Source: '{Source}', Outcome: '{Outcome}', ElapsedMs: '{ElapsedMs}'",
                useDrive ? "GoogleDriveApi" : "DocumentProvider", outcome, elapsed.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            status!.Text = previousStatus;
            Log.Information("Android snapshot authorization or download canceled");
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
                .SetNeutralButton(useDrive ? "Choose Drive snapshot" : "Choose snapshot folder", (_, _) =>
                {
                    if (useDrive)
                    {
                        ChooseDriveSnapshot();
                    }
                    else
                    {
                        PickSnapshotFolder();
                    }
                })!
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

    private async Task CopyProviderSnapshotAsync(global::Android.Net.Uri folder, string archivePath, CancellationToken cancellationToken)
    {
        var resolver = ContentResolver!;
        var folderId = DocumentsContract.GetTreeDocumentId(folder);
        using var children = DocumentsContract.BuildChildDocumentsUriUsingTree(folder, folderId)
            ?? throw new IOException("The snapshot folder could not be opened.");
        using var rows = resolver.Query(children,
            [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName,
             DocumentsContract.Document.ColumnMimeType], null, null, null)
            ?? throw new IOException("The snapshot folder could not be listed.");

        string? documentId = null;
        while (rows.MoveToNext())
        {
            if (rows.GetString(1) != CloseTimeExporter.SnapshotFileName ||
                rows.GetString(2) == DocumentsContract.Document.MimeTypeDir)
            {
                continue;
            }
            if (documentId is not null)
            {
                throw new IOException("More than one Snapshot.balancia was found in the selected folder.");
            }

            documentId = rows.GetString(0);
        }

        if (documentId is null)
        {
            throw new FileNotFoundException("Snapshot.balancia was not found in the selected folder.");
        }

        using var document = DocumentsContract.BuildDocumentUriUsingTree(folder, documentId)
            ?? throw new IOException("The snapshot document could not be identified.");
        await using var input = resolver.OpenInputStream(document)
            ?? throw new IOException("The snapshot document could not be opened.");
        await StageSnapshotAsync(input, archivePath, cancellationToken);
    }

    private static async Task StageSnapshotAsync(Stream input, string archivePath, CancellationToken cancellationToken)
    {
        await using var output = File.Create(archivePath);
        await input.CopyToAsync(output, cancellationToken);
    }
}
