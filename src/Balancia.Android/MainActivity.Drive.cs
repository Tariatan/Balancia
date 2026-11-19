using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Serilog;
using System.Net;
using System.Net.Http.Headers;
using PlayServicesTask = Android.Gms.Tasks.Task;

namespace Balancia.Android;

public partial class MainActivity
{
    private const int DriveAuthorizationRequest = 1004;
    private const int DriveConsentPrompt = 1;
    private const string DriveSnapshotPreference = "drive-snapshot-id";
    private const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    private static readonly HttpClient DriveHttp = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string? driveSnapshotId;
    private TaskCompletionSource<AuthorizationResult>? driveAuthorization;

    private async void ChooseDriveSnapshot() => await LoadSnapshot(null, rememberSource: true);

    private async Task<string> DownloadDriveSnapshotAsync(string? fileId, string archivePath, CancellationToken cancellationToken)
    {
        var selecting = string.IsNullOrEmpty(fileId);
        using var client = Identity.GetAuthorizationClient(this);
        using var scope = new Scope(DriveFileScope);
        using var builder = AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes([scope])
            .SetOptOutIncludingGrantedScopes(true);
        if (selecting)
        {
            builder.SetPrompt(DriveConsentPrompt);
            builder.AddResourceParameter(AuthorizationRequest.ResourceParameter.PickerOauthTrigger, "true");
        }

        using var request = builder.Build();
        var completion = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new DriveAuthorizationListener(completion);
        using var authorization = client.Authorize(request);
        authorization.AddOnCompleteListener(this, listener);
        using var initial = await completion.Task.WaitAsync(cancellationToken);
        using var resolved = initial.HasResolution ? await ResolveDriveAuthorizationAsync(initial, cancellationToken) : null;
        var result = resolved ?? initial;

        if (selecting)
        {
            var selected = result.TokenResponseParams?.GetString("picked_file_ids");
            if (string.IsNullOrEmpty(selected) || selected.Contains(','))
            {
                throw new OperationCanceledException("No Drive snapshot was selected.");
            }

            fileId = selected;
        }

        var token = result.AccessToken ?? throw new IOException("Google Drive did not grant access to the snapshot.");
        using var download = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId!)}?alt=media&supportsAllDrives=true");
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        download.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await DriveHttp.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Log.Information("Android Google Drive download rejected, StatusCode: '{StatusCode}'", (int)response.StatusCode);
        }
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            throw new IOException("Drive could not open the saved snapshot. Choose it again if it was replaced or access changed.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new IOException($"Google Drive download failed (HTTP {(int)response.StatusCode}).");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await StageSnapshotAsync(input, archivePath, cancellationToken);
        return fileId!;
    }

    private async Task<AuthorizationResult> ResolveDriveAuthorizationAsync(AuthorizationResult result, CancellationToken cancellationToken)
    {
        using var pending = result.PendingIntent ?? throw new IOException("Google Drive authorization could not be opened.");
        using var sender = pending.IntentSender;
        var completion = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        driveAuthorization = completion;
        try
        {
            StartIntentSenderForResult(sender, DriveAuthorizationRequest, null, 0, 0, 0);
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            driveAuthorization = null;
        }
    }

    private void CompleteDriveAuthorization(Result resultCode, Intent? data)
    {
        var completion = driveAuthorization;
        if (completion is null)
        {
            return;
        }
        if (resultCode != Result.Ok || data is null)
        {
            completion.TrySetCanceled();
            return;
        }

        try
        {
            using var client = Identity.GetAuthorizationClient(this);
            completion.TrySetResult(client.GetAuthorizationResultFromIntent(data));
        }
        catch (ApiException ex)
        {
            completion.TrySetException(new IOException($"Google Drive authorization failed (code {ex.StatusCode})."));
        }
    }

    private void SaveDriveSnapshotSelection(string fileId)
    {
        using var preferences = GetSharedPreferences("viewer", FileCreationMode.Private)!;
        using var editor = preferences.Edit()!;
        editor.PutString(DriveSnapshotPreference, fileId);
        if (!editor.Commit())
        {
            Log.Warning("Android Google Drive selection could not be saved");
        }
    }

    private sealed class DriveAuthorizationListener(TaskCompletionSource<AuthorizationResult> completion)
        : Java.Lang.Object, global::Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(PlayServicesTask task)
        {
            if (task.IsSuccessful && task.Result is AuthorizationResult result)
            {
                completion.TrySetResult(result);
            }
            else if (task.IsCanceled)
            {
                completion.TrySetCanceled();
            }
            else
            {
                var status = (task.Exception as ApiException)?.StatusCode;
                completion.TrySetException(new IOException($"Google Drive authorization failed (code {status?.ToString() ?? "unknown"})."));
            }
        }
    }
}
