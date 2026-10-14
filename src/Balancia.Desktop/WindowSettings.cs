using Avalonia;
using Avalonia.Controls;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const double MaxWindowDimension = 10000;

    private sealed record WindowSettings(double Width, double Height, int X, int Y);
    private WindowSettings? windowSettings;
    private PixelPoint? windowPositionForPersistence;

    private void LoadWindowSettings(ApplicationSettings? settings)
    {
        if (settings?.Window is not { } geometry || !IsValidWindowSize(geometry.Width, geometry.Height))
        {
            return;
        }

        windowSettings = geometry;
        Width = geometry.Width;
        Height = geometry.Height;
        windowPositionForPersistence = new PixelPoint(geometry.X, geometry.Y);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    // Called from Opened: restores the position stored in the loaded settings (windowSettings is only replaced on close).
    private void ApplyLoadedWindowPosition()
    {
        if (windowSettings is { } geometry)
        {
            Position = new PixelPoint(geometry.X, geometry.Y);
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            var position = windowPositionForPersistence ?? Position;
            windowSettings = new WindowSettings(Width, Height, position.X, position.Y);
            SaveApplicationSettings(DatabasePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Unable to save window settings, SettingsPath: '{SettingsPath}'", applicationSettingsPath);
        }
    }

    private bool IsValidWindowSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) &&
        width >= MinWidth && height >= MinHeight &&
        width <= MaxWindowDimension && height <= MaxWindowDimension;
}
