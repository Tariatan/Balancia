using System.Text.Json;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private sealed record ApplicationSettings(string DatabasePath, string? BackupPath = null, string? SnapshotPath = null, string? DefaultAccountId = null);

    private static ApplicationSettings? LoadApplicationSettings(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path))
                : null;
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
    }

    private static string? ResolveFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void SaveApplicationSettings(string dbPath, string? selectedBackupPath = null, string? selectedSnapshotPath = null, string? selectedDefaultAccountId = null)
    {
        var directory = Path.GetDirectoryName(applicationSettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("The application settings folder is unavailable.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = applicationSettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(new ApplicationSettings(
            dbPath,
            selectedBackupPath ?? backupPath,
            selectedSnapshotPath ?? snapshotPath,
            selectedDefaultAccountId ?? defaultAccountId));
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, applicationSettingsPath, true);
    }
}
