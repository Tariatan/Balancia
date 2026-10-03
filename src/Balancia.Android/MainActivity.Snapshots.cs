using Android.Text;
using Balancia.Storage;
using Serilog;

namespace Balancia.Android;

public partial class MainActivity
{
    private string KeyPath => Path.Combine(NoBackupFilesDir!.AbsolutePath, "snapshot-key.keystore");

    private async Task ImportSnapshot(string archivePath)
    {
        var info = await Task.Run(() => SnapshotEncryption.ReadInfo(archivePath));
        Log.Information("Android snapshot format detected, Encrypted: '{Encrypted}'", info is not null);
        SnapshotKey? key = null;
        string? unlockError = null;
        try
        {
            if (info is not null)
            {
                try
                {
                    key = await Task.Run(() => AndroidSnapshotKeyStore.Load(KeyPath));
                    Log.Information("Android remembered snapshot key loaded, KeyAvailable: '{KeyAvailable}'", key is not null);
                }
                catch (Exception ex)
                {
                    Log.Warning("Android remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                    // Reinstallation, key invalidation, or a damaged cache requires pairing again.
                    status!.Text = "Enter the passphrase to unlock this snapshot again.";
                }

                if (key is not null && !key.Matches(info))
                {
                    Log.Information("Android remembered key does not match snapshot; pairing required");
                    key.Dispose();
                    key = null;
                }
                else if (key is not null)
                {
                    Log.Information("Android snapshot unlock using remembered key");
                }
            }

            while (true)
            {
                if (info is not null && key is null)
                {
                    Log.Information("Android snapshot unlock prompting for passphrase");
                    var passphrase = await AskPassphrase(unlockError);
                    if (passphrase is null)
                    {
                        Log.Information("Android snapshot unlock canceled; previous copy unchanged");
                        status!.Text = "Snapshot unlock canceled. The previous copy is unchanged.";
                        return;
                    }

                    key = await Task.Run(() => SnapshotKey.Derive(passphrase, info));
                }

                try
                {
                    var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
                    var (importedStore, manifest) = await Task.Run(() => SnapshotImporter.Import(archivePath, dbPath, key));
                    viewerStore = importedStore;
                    await DisplaySnapshot($"Revision {manifest.Revision}");
                    if (key is not null)
                    {
                        try
                        {
                            await Task.Run(() => AndroidSnapshotKeyStore.Save(KeyPath, key));
                            Log.Information("Android snapshot key remembered with Keystore");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("Android snapshot loaded but key could not be remembered, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                            status!.Text += "\nSnapshot loaded, but the key could not be remembered. Next import will ask again.";
                        }
                    }

                    return;
                }
                catch (InvalidDataException ex) when (info is not null &&
                    ex.Message == "The snapshot passphrase is incorrect or the file is damaged.")
                {
                    Log.Information("Android snapshot unlock rejected; retrying passphrase entry");
                    key?.Dispose();
                    key = null;
                    status!.Text = ex.Message;
                    unlockError = ex.Message;
                }
            }
        }
        finally
        {
            key?.Dispose();
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
        var snapshot = await Task.Run(() => viewerStore!.ReadSnapshot());
        Log.Information("Android snapshot summary refreshed");
        status!.Text = $"{label} · Net worth {snapshot.NetWorth.Francs:N2}\n" +
            $"Income {snapshot.MonthlyIncome.Francs:N2} · Expenses {snapshot.MonthlyExpenses.Francs:N2}\n" +
            $"{snapshot.Entries.Count} transactions · read-only viewer";
    }
}
