using System.ComponentModel;
using Avalonia.Controls;
using Balancia.Storage;
using Serilog;
using Serilog.Context;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private SnapshotKey? snapshotKey;
    private string SnapshotKeyPath => Path.Combine(
        Path.GetDirectoryName(separateDataFolder ? DatabasePath : applicationSettingsPath)!,
        "snapshot-key.dpapi");
    internal bool EncryptionConfigured => snapshotKey is not null;

    /// <summary>Provides the snapshot key without the Windows key store or the passphrase dialog; used by UI tests.</summary>
    internal void UseSnapshotKey(SnapshotKey key)
    {
        snapshotKey?.Dispose();
        snapshotKey = key;
    }

    private async Task InitializeEncryption()
    {
        Log.Information("Loading remembered Windows snapshot key");
        try
        {
            snapshotKey ??= await Task.Run(() => WindowsSnapshotKeyStore.Load(SnapshotKeyPath));
            Log.Information("Windows snapshot key load completed, KeyAvailable: '{KeyAvailable}'", snapshotKey is not null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warning("Remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            await ShowEncryptionError(this, "The remembered snapshot key is unavailable. Set up encryption again before exporting.");
        }

        if (snapshotKey is null && (BackupPath is not null || SnapshotPath is not null) && !await ConfigureEncryption(this))
        {
            Log.Information("Automatic snapshot and backup exports paused; encryption is not configured");
            await ShowEncryptionError(this, "Automatic backups and snapshots are paused until a passphrase is configured in Settings.");
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

    private async Task<bool> EnsureEncryption(Window dialogOwner) => snapshotKey is not null || await ConfigureEncryption(dialogOwner);
}
