using Android.Text;
using Balancia.Storage;
using Serilog;

namespace Balancia.Android;

public partial class MainActivity
{
    private string KeyPath => Path.Combine(NoBackupFilesDir!.AbsolutePath, "snapshot-key.keystore");

    private async Task ImportSnapshot(string archivePath)
    {
        var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
        var outcome = await ViewerSnapshotImport.RunAsync(archivePath, dbPath, new ImportHost(this));
        switch (outcome)
        {
            case ViewerImportOutcome.Canceled:
                status!.Text = "Snapshot unlock canceled. The previous copy is unchanged.";
                break;
            case ViewerImportOutcome.ImportedKeyNotRemembered:
                status!.Text += "\nSnapshot loaded, but the key could not be remembered. Next import will ask again.";
                break;
        }
    }

    /// <summary>The Android parts of the import: the Keystore-protected key cache, the passphrase dialog and the status screen.</summary>
    private sealed class ImportHost(MainActivity activity) : IViewerSnapshotHost
    {
        public SnapshotKey? LoadRememberedKey() => AndroidSnapshotKeyStore.Load(activity.KeyPath);

        public void RememberKey(SnapshotKey key) => AndroidSnapshotKeyStore.Save(activity.KeyPath, key);

        public Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error)
        {
            if (reason == PassphraseReason.RememberedKeyUnavailable)
            {
                activity.status!.Text = "Enter the passphrase to unlock this snapshot again.";
            }
            else if (reason == PassphraseReason.WrongPassphrase)
            {
                activity.status!.Text = error;
            }

            return activity.AskPassphrase(error);
        }

        public async Task ImportedAsync(LedgerReader reader, SnapshotManifest manifest)
        {
            activity.viewerReader = reader;
            await activity.DisplaySnapshot($"Revision {manifest.Revision}");
        }
    }

    private Task<string?> AskPassphrase(string? unlockError)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new EditText(this)
        {
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword,
            Hint = "Snapshot passphrase"
        };
        input.SetSingleLine(true);
        var dialog = new AlertDialog.Builder(this)
            .SetTitle("Unlock snapshot")!
            .SetMessage(unlockError ?? "Enter the Windows export passphrase. Android remembers the key for later imports.")!
            .SetView(input)!
            .SetPositiveButton("Unlock", (_, _) => completion.TrySetResult(input.Text))!
            .SetNegativeButton("Cancel", (_, _) => completion.TrySetResult(null))!
            .Create()!;
        dialog.DismissEvent += (_, _) =>
        {
            completion.TrySetResult(null);
            input.Text = "";
            input.Dispose();
            dialog.Dispose();
        };
        dialog.Show();
        input.RequestFocus();
        return completion.Task;
    }

    private async Task DisplaySnapshot(string label)
    {
        await Task.Run(() => viewerReader!.ReadIdentity());
        var imported = File.GetLastWriteTime(Path.Combine(FilesDir!.AbsolutePath, "viewer.db"));
        Log.Information("Android snapshot summary refreshed");
        status!.Text = $"Imported {imported:dd MMM yyyy HH:mm} · {label} · Read only";
        RefreshDashboard();
    }
}
