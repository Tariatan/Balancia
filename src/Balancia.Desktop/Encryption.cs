using Avalonia.Controls;
using Balancia.Storage;
using Serilog;
using Serilog.Context;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly SnapshotKeyService keys;
    private string SnapshotKeyPath => Path.Combine(
        Path.GetDirectoryName(separateDataFolder ? DatabasePath : settingsStore.Path)!,
        "snapshot-key.dpapi");
    internal bool EncryptionConfigured => keys.IsConfigured;

    /// <summary>Provides the snapshot key without the Windows key store or the passphrase dialog; used by UI tests.</summary>
    internal void UseSnapshotKey(SnapshotKey key) => keys.Use(key);

    private async Task InitializeEncryption()
    {
        var load = await Task.Run(keys.Load);
        if (load == KeyLoadResult.Unavailable)
        {
            await ShowEncryptionError(this, "The remembered snapshot key is unavailable. Set up encryption again before exporting.");
        }

        if (!keys.IsConfigured && (BackupPath is not null || SnapshotPath is not null) && !await ConfigureEncryption(this))
        {
            Log.Information("Automatic snapshot and backup exports paused; encryption is not configured");
            await ShowEncryptionError(this, "Automatic backups and snapshots are paused until a passphrase is configured in Settings.");
        }
    }

    internal async Task<bool> ConfigureEncryption(Window dialogOwner)
    {
        using var context = LogContext.PushProperty("EncryptionSetupId", Guid.NewGuid().ToString("N"));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Log.Information("Snapshot encryption setup requested, ReplacingKey: '{ReplacingKey}'", keys.IsConfigured);
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
            await Task.Run(() => keys.Replace(replacement));
            replacement = null;
            Log.Information("Snapshot encryption configured and key remembered with Windows DPAPI, ElapsedMs: '{ElapsedMs}'", elapsed.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot encryption setup failed; previous settings retained, FailureType: '{FailureType}', HResult: '{HResult}', ElapsedMs: '{ElapsedMs}'",
                ex.GetType().Name, ex.HResult, elapsed.ElapsedMilliseconds);
            await ShowEncryptionError(dialogOwner, "The snapshot key could not be saved. Encryption settings were kept.");
            return false;
        }
        finally
        {
            replacement?.Dispose();
        }
    }

    private static Task ShowEncryptionError(Window dialogOwner, string message) =>
        ErrorDialog.Show(dialogOwner, Get("Snapshot encryption"), Get(message));

    private async Task<bool> EnsureEncryption(Window dialogOwner) => keys.IsConfigured || await ConfigureEncryption(dialogOwner);
}
