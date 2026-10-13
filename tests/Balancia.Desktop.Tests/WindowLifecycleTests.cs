using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class WindowLifecycleTests
{
    [AvaloniaFact]
    public async Task Close_WhileTheLedgerIsStillLoading_KeepsTheWindowOpenUntilLoadingFinishes()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        var window = workspace.CreateSeparateFolderWindow();
        var overview = window.Find<ContentControl>("Overview");
        window.Show();
        Assert.False(overview.IsEnabled); // the startup load is running

        // Act
        window.Close();
        var stayedOpenWhileLoading = window.IsVisible;
        await DesktopWorkspace.WaitUntilAsync(() => overview.IsVisible && overview.IsEnabled, "the overview to finish loading");
        window.Close();

        // Assert
        Assert.True(stayedOpenWhileLoading);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Close_WhileLoading_WritesNoBackupUntilItReallyCloses()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: true);
        var overview = window.Find<ContentControl>("Overview");
        window.Show();

        // Act
        window.Close();
        var backupsWhileLoading = Directory.Exists(workspace.BackupDirectory)
            ? Directory.GetFiles(workspace.BackupDirectory).Length
            : 0;
        await DesktopWorkspace.WaitUntilAsync(() => overview.IsVisible && overview.IsEnabled, "the overview to finish loading");
        window.Close();

        // Assert
        Assert.Equal(0, backupsWhileLoading);
        Assert.Single(Directory.GetFiles(workspace.BackupDirectory, "backup-*.balancia"));
    }
}
