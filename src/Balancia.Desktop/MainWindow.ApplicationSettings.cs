using System.Text.Json;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly object settingsWriteGate = new();

    private sealed record ApplicationSettings(string DatabasePath, string? BackupPath = null, string? SnapshotPath = null,
        string? DefaultAccountId = null, string? Language = null, WindowSettings? Window = null);

    private static ApplicationSettings? LoadApplicationSettings(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path))
                : null;
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(ex, "Unable to load application settings, SettingsPath: '{SettingsPath}'", path);
            return null;
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(ex, "Unable to load application settings, SettingsPath: '{SettingsPath}'", path);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Serilog.Log.Warning(ex, "Unable to load application settings, SettingsPath: '{SettingsPath}'", path);
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

    private void SaveApplicationSettings(string dbPath, string? selectedBackupPath = null, string? selectedSnapshotPath = null,
        string? selectedDefaultAccountId = null, string? selectedLanguage = null)
    {
        if (separateDataFolder)
        {
            return;
        }

        lock (settingsWriteGate)
        {
            var directory = Path.GetDirectoryName(applicationSettingsPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new IOException("The application settings folder is unavailable.");
            }

            Directory.CreateDirectory(directory);
            var settingsPath = applicationSettingsPath;
            var temporaryPath = settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(new ApplicationSettings(
                dbPath,
                selectedBackupPath ?? backupPath,
                selectedSnapshotPath ?? snapshotPath,
                selectedDefaultAccountId ?? defaultAccountId,
                selectedLanguage ?? languagePreference,
                windowSettings));
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, settingsPath, true);
        }
    }
}
