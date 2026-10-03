using Avalonia.Controls;
using Balancia.Storage;
using Serilog;
using Serilog.Context;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private SnapshotKey? snapshotKey;
    private string SnapshotKeyPath => separateDataFolder
        ? Path.Combine(Path.GetDirectoryName(databasePath)!, "snapshot-key.dpapi")
        : Path.Combine(Path.GetDirectoryName(applicationSettingsPath)!, "snapshot-key.dpapi");
    internal bool EncryptionConfigured => snapshotKey is not null;

    private async Task InitializeEncryption()
    {
        Log.Information("Loading remembered Windows snapshot key");
        try
        {
            snapshotKey = await Task.Run(() => WindowsSnapshotKeyStore.Load(SnapshotKeyPath));
            Log.Information("Windows snapshot key load completed, KeyAvailable: '{KeyAvailable}'", snapshotKey is not null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or
            UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warning("Remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            await ShowErrorDialog("Snapshot encryption", Get("The remembered snapshot key is unavailable. Set up encryption again before exporting."));
        }

        if (snapshotKey is null && (backupPath is not null || snapshotPath is not null))
        {
            if (!await ConfigureEncryption(this))
            {
                Log.Information("Automatic snapshot and backup exports paused; encryption is not configured");
                await ShowErrorDialog("Snapshot encryption", Get("Automatic backups and snapshots are paused until a passphrase is configured in Settings."));
            }
        }
    }

    internal async Task<bool> ConfigureEncryption(Window dialogOwner)
    {
        using var context = LogContext.PushProperty("EncryptionSetupId", Guid.NewGuid().ToString("N"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Log.Information("Snapshot encryption setup requested, ReplacingKey: '{ReplacingKey}'", snapshotKey is not null);
        var passphrase = await SnapshotPassphraseWindow.Ask(dialogOwner, setup: true);
        if (passphrase is null)
        {
            Log.Information("Snapshot encryption setup canceled");
            return false;
        }

        SnapshotKey? replacement = null;
        try
        {
            replacement = await Task.Run(() => SnapshotKey.Create(passphrase));
            await Task.Run(() => WindowsSnapshotKeyStore.Save(SnapshotKeyPath, replacement));
            snapshotKey?.Dispose();
            snapshotKey = replacement;
            replacement = null;
            Log.Information("Snapshot encryption configured and key remembered with Windows DPAPI, ElapsedMs: '{ElapsedMs}'", elapsed.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot encryption setup failed; previous settings retained, FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
            await ErrorDialog.Show(dialogOwner, Get("Snapshot encryption"), Get("The snapshot key could not be saved. Encryption settings were kept."));
            return false;
        }
        finally
        {
            replacement?.Dispose();
        }
    }

    private async Task<bool> EnsureEncryption(Window dialogOwner) => snapshotKey is not null || await ConfigureEncryption(dialogOwner);
}
