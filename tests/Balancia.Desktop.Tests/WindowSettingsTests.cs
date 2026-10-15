using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class WindowSettingsTests
{
    [AvaloniaFact]
    public async Task Close_AfterResizing_SavesTheWindowSize()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        window.Width = 1500;
        window.Height = 900;

        // Act
        window.Close();

        // Assert
        var saved = workspace.ReadSavedWindow();
        Assert.NotNull(saved);
        Assert.Equal(1500, saved.Value.GetProperty("Width").GetDouble());
        Assert.Equal(900, saved.Value.GetProperty("Height").GetDouble());
    }

    [AvaloniaFact]
    public async Task Close_AfterMoving_SavesTheWindowPosition()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        window.Position = new PixelPoint(210, 140);

        // Act
        window.Close();

        // Assert
        var saved = workspace.ReadSavedWindow();
        Assert.NotNull(saved);
        Assert.Equal(210, saved.Value.GetProperty("X").GetInt32());
        Assert.Equal(140, saved.Value.GetProperty("Y").GetInt32());
    }

    [AvaloniaFact]
    public async Task Open_WithSavedGeometry_RestoresSizeAndPosition()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(window: (1500, 900, 120, 80));
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: false);

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Equal(1500, window.Width);
        Assert.Equal(900, window.Height);
        Assert.Equal(new PixelPoint(120, 80), window.Position);
    }

    [AvaloniaTheory]
    [InlineData(100, 100)]
    [InlineData(20000, 900)]
    [InlineData(1500, 20000)]
    public void Create_WithImplausibleSavedSize_KeepsTheDefaultSize(double width, double height)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(window: (width, height, 0, 0));

        // Act
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: false);

        // Assert
        // Checked before the window is shown: once shown, the platform clamps the size whatever the setting was.
        Assert.NotEqual(width, window.Width);
        Assert.True(window.Width >= window.MinWidth);
        Assert.True(window.Height >= window.MinHeight);
        Assert.True(window.Width <= 10000);
        Assert.True(window.Height <= 10000);
    }
}
