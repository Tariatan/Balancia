using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>Covers the best-effort backup and snapshot writes done when the main window closes.</summary>
public sealed class BackupOnCloseTests
{
    [AvaloniaFact]
    public async Task Close_WithBackupFolderAndKey_WritesOneValidEncryptedBackup()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        window.Close();

        // Assert
        var backup = Assert.Single(Directory.GetFiles(workspace.BackupDirectory, "backup-*.balancia"));
        Assert.StartsWith("balancia-snapshot", workspace.ValidateExport(backup).Format);
    }

    [AvaloniaFact]
    public async Task Close_WithoutBackupFolder_WritesNoBackup()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        window.Close();

        // Assert
        Assert.False(Directory.Exists(workspace.BackupDirectory));
    }

    [AvaloniaFact]
    public void Close_WithBackupFolderButNoKey_SkipsBackup()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory, snapshotPath: workspace.SnapshotDirectory);
        var window = workspace.CreateConfiguredWindow(withSnapshotKey: false);

        // Act
        window.Close();

        // Assert
        Assert.False(Directory.Exists(workspace.BackupDirectory));
        Assert.False(Directory.Exists(workspace.SnapshotDirectory));
    }

    [AvaloniaFact]
    public async Task Close_WithTwelveExistingBackups_KeepsTheTenNewest()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        Directory.CreateDirectory(workspace.BackupDirectory);
        var existing = Enumerable.Range(1, 12).Select(index =>
        {
            var path = Path.Combine(workspace.BackupDirectory, $"backup-existing-{index:00}.balancia");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, index, 0, 0, 0, DateTimeKind.Utc));
            return path;
        }).ToArray();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        window.Close();

        // Assert
        var remaining = Directory.GetFiles(workspace.BackupDirectory, "backup-*.balancia");
        Assert.Equal(10, remaining.Length);
        Assert.All(existing.Take(3), path => Assert.DoesNotContain(path, remaining));
        Assert.All(existing.Skip(3), path => Assert.Contains(path, remaining));
        Assert.Single(remaining.Except(existing));
    }

    [AvaloniaFact]
    public async Task Close_WhenBackupFolderCannotBeCreated_StillClosesWithoutThrowing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        var blocker = Path.Combine(workspace.Root, "not-a-folder");
        File.WriteAllText(blocker, "a file where the backup folder should go");
        var unreachable = Path.Combine(blocker, "backups");
        workspace.WriteSettings(backupPath: unreachable);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        var closing = Record.Exception(window.Close);

        // Assert
        Assert.Null(closing);
        Assert.False(window.IsVisible);
        Assert.False(Directory.Exists(unreachable));
    }

    [AvaloniaFact]
    public async Task Close_WithSnapshotFolderAndKey_WritesValidSnapshot()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(snapshotPath: workspace.SnapshotDirectory);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        window.Close();

        // Assert
        var snapshot = Path.Combine(workspace.SnapshotDirectory, "Snapshot.balancia");
        Assert.True(File.Exists(snapshot));
        Assert.NotNull(workspace.ValidateExport(snapshot));
    }

    [AvaloniaFact]
    public async Task Close_InSeparateDataFolderMode_WritesNeitherBackupNorSettings()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        var window = workspace.CreateSeparateFolderWindow();
        window.UseSnapshotKey(DesktopWorkspace.CreateKey());
        await DesktopWorkspace.OpenAsync(window);

        // Act
        window.Close();

        // Assert
        Assert.False(Directory.Exists(workspace.BackupDirectory));
        Assert.False(File.Exists(workspace.SettingsFile));
    }
}
