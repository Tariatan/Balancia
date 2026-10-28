using System.Text.Json;
using Balancia.Storage;

namespace Balancia.Desktop;

internal sealed record WindowSettings(double Width, double Height, int X, int Y);

/// <summary>The persisted user preferences. A null path, language or theme means "not chosen".</summary>
internal sealed record ApplicationSettings(string DatabasePath, string? BackupPath = null, string? SnapshotPath = null,
    string? DefaultAccountId = null, string? Language = null, WindowSettings? Window = null, string? Theme = null);

/// <summary>Reads and atomically replaces the settings file.</summary>
internal sealed class SettingsStore(string path)
{
    private readonly object writeGate = new();

    public string Path { get; } = path;

    /// <summary>The saved settings, or null when the file is missing or unreadable.</summary>
    public ApplicationSettings? Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(Path))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Unable to load application settings, SettingsPath: '{SettingsPath}'", Path);
            return null;
        }
    }

    public void Save(ApplicationSettings settings)
    {
        lock (writeGate)
        {
            AtomicFileWriter.Write(Path, stream => JsonSerializer.Serialize(stream, settings));
        }
    }

    /// <summary>The absolute form of a saved path, or null when it is empty or malformed.</summary>
    public static string? ResolveFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
