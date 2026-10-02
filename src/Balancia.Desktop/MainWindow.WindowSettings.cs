using Avalonia;
using Avalonia.Controls;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private sealed record WindowSettings(double Width, double Height, int X, int Y);
    private WindowSettings? windowSettings;
    private PixelPoint? loadedWindowPosition;
    private PixelPoint? windowPositionForPersistence;

    private void LoadWindowSettings(ApplicationSettings? settings)
    {
        var geometry = settings?.Window;
        if (geometry is null || !IsValidWindowSize(geometry.Width, geometry.Height))
        {
            return;
        }

        windowSettings = geometry;
        Width = geometry.Width;
        Height = geometry.Height;
        loadedWindowPosition = new PixelPoint(geometry.X, geometry.Y);
        windowPositionForPersistence = loadedWindowPosition;
        WindowStartupLocation = WindowStartupLocation.Manual;
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
            var position = windowPositionForPersistence ?? Position;
            windowSettings = new WindowSettings(Width, Height, position.X, position.Y);
            SaveApplicationSettings(databasePath);
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
