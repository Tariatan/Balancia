using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private sealed record WindowSettings(double Width, double Height, int X, int Y);
    private PixelPoint? loadedWindowPosition;
    private PixelPoint? windowPositionForPersistence;

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
