using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private sealed record WindowSettings(double Width, double Height, int X, int Y);
    private sealed record ApplicationSettings(string DatabasePath, string? BackupPath = null, string? SnapshotPath = null, string? DefaultAccountId = null);
    private PixelPoint? loadedWindowPosition;
    private PixelPoint? windowPositionForPersistence;

    private static string? LoadSavedDatabasePath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path));
            if (settings is null || string.IsNullOrWhiteSpace(settings.DatabasePath))
            {
                return null;
            }

            return Path.GetFullPath(settings.DatabasePath);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? LoadSavedBackupPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path));
            if (string.IsNullOrWhiteSpace(settings?.BackupPath))
            {
                return null;
            }

            return Path.GetFullPath(settings.BackupPath);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static string? LoadSavedSnapshotPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path));
            if (string.IsNullOrWhiteSpace(settings?.SnapshotPath))
            {
                return null;
            }

            return Path.GetFullPath(settings.SnapshotPath);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static string? LoadSavedDefaultAccountId(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path))?.DefaultAccountId;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void SaveApplicationSettings(string databasePath, string? selectedBackupPath = null, string? selectedSnapshotPath = null, string? selectedDefaultAccountId = null)
    {
        var directory = Path.GetDirectoryName(applicationSettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("The application settings folder is unavailable.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = applicationSettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(new ApplicationSettings(
            databasePath,
            selectedBackupPath ?? backupPath,
            selectedSnapshotPath ?? snapshotPath,
            selectedDefaultAccountId ?? defaultAccountId));
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, applicationSettingsPath, true);
    }

    private void SaveBackupOnClose()
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(backupPath);
            var fileName = $"balancia-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.balancia";
            store.ExportSnapshot(Path.Combine(backupPath, fileName));
            var backups = Directory.EnumerateFiles(backupPath, "balancia-backup-*.balancia")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            foreach (var oldBackup in backups.Take(Math.Max(0, backups.Length - 10)))
            {
                File.Delete(oldBackup);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
    }

    private void SaveSnapshotOnClose()
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(snapshotPath);
            store.ExportSnapshot(Path.Combine(snapshotPath, "Snapshot.balancia"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
    }

    private void LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(windowSettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(windowSettingsPath);
            var settings = JsonSerializer.Deserialize<WindowSettings>(json);
            if (settings is null || !IsValidWindowSize(settings.Width, settings.Height))
            {
                return;
            }

            Width = settings.Width;
            Height = settings.Height;
            loadedWindowPosition = new PixelPoint(settings.X, settings.Y);
            windowPositionForPersistence = loadedWindowPosition;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        catch (JsonException)
        {
            // A malformed settings file should not prevent the ledger from opening.
        }
        catch (IOException)
        {
            // Window settings are optional and can be unavailable temporarily.
        }
        catch (UnauthorizedAccessException)
        {
            // Window settings are optional and can be unavailable temporarily.
        }
    }

    private void ApplyLoadedWindowPosition()
    {
        if (loadedWindowPosition is { } position)
        {
            Position = position;
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(windowSettingsPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var position = windowPositionForPersistence ?? Position;
            var settings = new WindowSettings(Width, Height, position.X, position.Y);
            var json = JsonSerializer.Serialize(settings);
            var temporaryPath = windowSettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, windowSettingsPath, true);
        }
        catch (IOException)
        {
            // Window settings are best effort and must not affect application shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // Window settings are best effort and must not affect application shutdown.
        }
    }

    private bool IsValidWindowSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) &&
        width >= MinWidth && height >= MinHeight &&
        width <= 10000 && height <= 10000;
}
