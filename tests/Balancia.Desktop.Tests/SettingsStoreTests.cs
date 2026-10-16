using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-settings-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "nested", "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        // Arrange
        var store = new SettingsStore(FilePath);

        // Act
        var loaded = store.Load();

        // Assert
        Assert.Null(loaded);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void Load_UnreadableContent_ReturnsNull(string content)
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
        var store = new SettingsStore(FilePath);

        // Act
        var loaded = store.Load();

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryFieldAndCreatesTheFolder()
    {
        // Arrange
        var store = new SettingsStore(FilePath);
        var settings = new ApplicationSettings("/data/balancia.db", "/backups", "/snapshots", "account-1", "de", new WindowSettings(900, 700, 10, 20));

        // Act
        store.Save(settings);
        var loaded = store.Load();

        // Assert
        Assert.Equal(settings, loaded);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp-*"));
    }

    [Fact]
    public void Save_PathsSetToNull_ClearsThemInTheFile()
    {
        // Arrange
        var store = new SettingsStore(FilePath);
        store.Save(new ApplicationSettings("/data/balancia.db", "/backups", "/snapshots"));

        // Act
        store.Save(new ApplicationSettings("/data/balancia.db"));
        var loaded = store.Load();

        // Assert
        Assert.NotNull(loaded);
        Assert.Null(loaded.BackupPath);
        Assert.Null(loaded.SnapshotPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path")]
    public void ResolveFullPath_EmptyOrMalformed_ReturnsNull(string? path)
    {
        // Arrange
        // The input is the only fixture.

        // Act
        var resolved = SettingsStore.ResolveFullPath(path);

        // Assert
        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveFullPath_RelativePath_ReturnsAbsolutePath()
    {
        // Arrange
        var relative = Path.Combine("some", "folder");

        // Act
        var resolved = SettingsStore.ResolveFullPath(relative);

        // Assert
        Assert.Equal(Path.GetFullPath(relative), resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
