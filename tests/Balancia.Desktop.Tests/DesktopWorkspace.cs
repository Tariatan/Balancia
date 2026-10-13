using System.Buffers.Binary;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Balancia.Storage;

namespace Balancia.Desktop.Tests;

/// <summary>
/// A temporary data folder plus settings file, so a <see cref="MainWindow"/> never reads or writes the user's real settings.
/// The language is pinned to English so text assertions do not depend on the machine culture.
/// </summary>
internal sealed class DesktopWorkspace : IDisposable
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);
    public DesktopWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "balancia-ui-" + Guid.NewGuid().ToString("N"));
        DataDirectory = Path.Combine(Root, "data");
        SettingsFile = Path.Combine(Root, "settings", "settings.json");
        BackupDirectory = Path.Combine(Root, "backups");
        SnapshotDirectory = Path.Combine(Root, "snapshots");
        Directory.CreateDirectory(DataDirectory);
    }

    public string Root { get; }

    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string BackupDirectory { get; }

    public string SnapshotDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "balancia.db");

    /// <summary>Creates the ledger schema and returns a store for seeding data before the window opens.</summary>
    public LedgerStore SeedStore()
    {
        var store = new LedgerStore(DatabasePath);
        store.Initialize();
        return store;
    }

    /// <summary>Writes the settings file the window reads at startup, as a regular (non separate-folder) installation.</summary>
    public void WriteSettings(string? backupPath = null, string? snapshotPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new
        {
            DatabasePath,
            BackupPath = backupPath,
            SnapshotPath = snapshotPath,
            Language = "en",
        }));
    }

    /// <summary>A window in <c>--data-dir</c> mode: settings, backups and snapshots are disabled.</summary>
    public MainWindow CreateSeparateFolderWindow() => new(DataDirectory, SettingsFile);

    /// <summary>A window configured from <see cref="WriteSettings"/>, with an optional snapshot key already provisioned.</summary>
    public MainWindow CreateConfiguredWindow(bool withSnapshotKey)
    {
        var window = new MainWindow(null, SettingsFile);
        if (withSnapshotKey)
        {
            window.UseSnapshotKey(CreateKey());
        }

        return window;
    }

    /// <summary>
    /// A fixed snapshot key imported from raw bytes, which skips the 600,000-iteration derivation. Every call returns a new
    /// instance with the same key material, because a window disposes the key it was given when it closes; use another
    /// instance to validate what the window exported.
    /// </summary>
    public static SnapshotKey CreateKey()
    {
        var secret = new byte[68];
        BinaryPrimitives.WriteInt32LittleEndian(secret, 600_000);
        for (var index = 4; index < secret.Length; index++)
        {
            secret[index] = (byte)index;
        }

        return SnapshotKey.ImportSecret(secret);
    }

    /// <summary>Validates an exported snapshot or backup with the same key material the window used.</summary>
    public SnapshotManifest ValidateExport(string path)
    {
        using var key = CreateKey();
        return new LedgerStore(DatabasePath).ValidateSnapshot(path, key);
    }

    /// <summary>Shows the window and waits until the startup load finished and the overview is interactive again.</summary>
    public static async Task OpenAsync(MainWindow window)
    {
        window.Show();
        var overview = window.FindControl<ContentControl>("Overview")!;
        await WaitUntilAsync(() => overview.IsVisible && overview.IsEnabled, "the overview to finish loading");
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + LoadTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
