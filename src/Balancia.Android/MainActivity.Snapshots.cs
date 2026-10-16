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
        SnapshotKey? key = null;
        string? unlockError = null;
        try
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

            while (true)
            {
                if (key is null)
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
                    var unlockedKey = key;
                    var dbPath = Path.Combine(FilesDir!.AbsolutePath, "viewer.db");
                    var (importedReader, manifest) = await Task.Run(() => SnapshotImporter.Import(archivePath, dbPath, unlockedKey));
                    viewerReader = importedReader;
                    await DisplaySnapshot($"Revision {manifest.Revision}");
                    try
                    {
                        await Task.Run(() => AndroidSnapshotKeyStore.Save(KeyPath, unlockedKey));
                        Log.Information("Android snapshot key remembered with Keystore");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Android snapshot loaded but key could not be remembered, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                        status!.Text += "\nSnapshot loaded, but the key could not be remembered. Next import will ask again.";
                    }

                    return;
                }
                catch (SnapshotAuthenticationException ex)
                {
                    Log.Information("Android snapshot unlock rejected; retrying passphrase entry");
                    key.Dispose();
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
        var summary = await Task.Run(() => viewerReader!.ReadSummary());
        var imported = File.GetLastWriteTime(Path.Combine(FilesDir!.AbsolutePath, "viewer.db"));
        Log.Information("Android snapshot summary refreshed");
        var accounts = string.Join("\n", summary.Accounts.Select(account => $"{account.Name}: {account.Balance.Francs:N2}"));
        status!.Text = $"{label} · Net worth {summary.NetWorth.Francs:N2}\n" +
            (accounts.Length == 0 ? string.Empty : accounts + "\n") +
            $"Income {summary.MonthlyIncome.Francs:N2} · Expenses {summary.MonthlyExpenses.Francs:N2}\n" +
            $"{summary.TransactionCount} transactions · imported {imported:yyyy-MM-dd HH:mm} · read-only viewer";
    }
}
