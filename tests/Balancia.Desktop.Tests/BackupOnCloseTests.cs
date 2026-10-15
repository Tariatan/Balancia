using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>Covers the best-effort backup and snapshot writes done when the main window closes.</summary>
public sealed class BackupOnCloseTests
{
    [AvaloniaFact]
    public async Task Close_FourChangedSessionsOnSameDay_ReplacesEarliestBackup()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        string? firstBackup = null;

        // Act
        for (var index = 0; index < 4; index++)
        {
            var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
            LedgerSeed.Account(store, $"Account {index}");
            window.Close();
            if (index == 0)
            {
                firstBackup = Assert.Single(Directory.GetFiles(workspace.BackupDirectory));
            }
        }

        // Assert
        var remaining = Directory.GetFiles(workspace.BackupDirectory);
        Assert.Equal(3, remaining.Length);
        Assert.DoesNotContain(firstBackup!, remaining);
        Assert.All(remaining, path => Assert.NotNull(workspace.ValidateExport(path)));
    }

    [AvaloniaFact]
    public async Task Close_WhenExportFails_PreservesAllExistingBackups()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        Directory.CreateDirectory(workspace.BackupDirectory);
        var existing = Enumerable.Range(0, 24).Select(index =>
        {
            var path = Path.Combine(workspace.BackupDirectory, $"backup-existing-{index}.balancia");
            File.WriteAllText(path, "old");
            return path;
        }).ToArray();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        var disposedKey = DesktopWorkspace.CreateKey();
        window.UseSnapshotKey(disposedKey);
        disposedKey.Dispose();

        // Act
        LedgerSeed.Account(store);
        window.Close();

        // Assert
        Assert.Equal(existing.Order(), Directory.GetFiles(workspace.BackupDirectory).Order());
        Assert.All(existing, path => Assert.Equal("old", File.ReadAllText(path)));
    }

    [AvaloniaFact]
    public async Task Close_AcrossRepeatedUnchangedSessions_PreservesExistingBackups()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
        window.Close();
        var backup = Assert.Single(Directory.GetFiles(workspace.BackupDirectory));
        var bytes = File.ReadAllBytes(backup);

        // Act
        for (var index = 0; index < 4; index++)
        {
            var reopened = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
            reopened.Close();
        }

        // Assert
        Assert.Equal(backup, Assert.Single(Directory.GetFiles(workspace.BackupDirectory)));
        Assert.Equal(bytes, File.ReadAllBytes(backup));
    }

    [AvaloniaFact]
    public async Task Close_WithMultipleBackupsPerDay_KeepsNewestThreeForEachDay()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        Directory.CreateDirectory(workspace.BackupDirectory);
        var existing = Enumerable.Range(0, 8).Select(index =>
        {
            var path = Path.Combine(workspace.BackupDirectory, $"backup-existing-{index}.balancia");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1 + index / 4, index % 4, 0, 0, DateTimeKind.Utc));
            return path;
        }).ToArray();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true, clock);

        // Act
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
        window.Close();

        // Assert
        var remaining = Directory.GetFiles(workspace.BackupDirectory, "backup-*.balancia");
        Assert.Equal(7, remaining.Length);
        Assert.DoesNotContain(existing[0], remaining);
        Assert.DoesNotContain(existing[4], remaining);
        Assert.All(existing.Where((_, index) => index % 4 != 0), path => Assert.Contains(path, remaining));
    }

    [AvaloniaFact]
    public async Task Close_WithBackupFolderAndKey_WritesOneValidEncryptedBackup()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
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
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
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
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
        window.Close();

        // Assert
        Assert.False(Directory.Exists(workspace.BackupDirectory));
        Assert.False(Directory.Exists(workspace.SnapshotDirectory));
    }

    [AvaloniaFact]
    public async Task Close_WithTwentyFourExistingBackups_KeepsTheTwentyOneNewest()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        Directory.CreateDirectory(workspace.BackupDirectory);
        var existing = Enumerable.Range(1, 24).Select(index =>
        {
            var path = Path.Combine(workspace.BackupDirectory, $"backup-existing-{index:00}.balancia");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, index, 0, 0, 0, DateTimeKind.Utc));
            return path;
        }).ToArray();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);

        // Act
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
        window.Close();

        // Assert
        var remaining = Directory.GetFiles(workspace.BackupDirectory, "backup-*.balancia");
        Assert.Equal(21, remaining.Length);
        Assert.All(existing.Take(4), path => Assert.DoesNotContain(path, remaining));
        Assert.All(existing.Skip(4), path => Assert.Contains(path, remaining));
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
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
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
        LedgerSeed.Account(new Balancia.Storage.LedgerStore(workspace.DatabasePath));
        window.Close();

        // Assert
        Assert.False(Directory.Exists(workspace.BackupDirectory));
        Assert.False(File.Exists(workspace.SettingsFile));
    }
}
