using System.Security.Cryptography;
using Serilog;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class KeyStoreAndLoggingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-keystore-" + Guid.NewGuid().ToString("N"));
    private string KeyFile => Path.Combine(directory, "snapshot-key.dpapi");

    public KeyStoreAndLoggingTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Load_NoKeyFile_ReturnsNull()
    {
        // Arrange
        var store = new WindowsSnapshotKeyStore();

        // Act
        var key = store.Load(KeyFile);

        // Assert
        Assert.Null(key);
    }

    [Fact]
    public void Load_OversizedKeyFile_ThrowsInvalidData()
    {
        // Arrange
        File.WriteAllBytes(KeyFile, new byte[20 * 1024]);

        // Act
        void Act() => new WindowsSnapshotKeyStore().Load(KeyFile);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void Load_ExistingFileOnAnotherPlatform_ThrowsPlatformNotSupported()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows can decrypt DPAPI blobs.");
        File.WriteAllBytes(KeyFile, new byte[64]);

        // Act
        void Act() => new WindowsSnapshotKeyStore().Load(KeyFile);

        // Assert
        Assert.Throws<PlatformNotSupportedException>(Act);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheKeyThroughDpapi()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is available only on Windows.");
        var store = new WindowsSnapshotKeyStore();
        using var original = DesktopWorkspace.CreateKey();

        // Act
        store.Save(KeyFile, original);
        using var loaded = store.Load(KeyFile);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(original.ExportSecret(), loaded.ExportSecret());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void Load_CorruptedProtectedBlob_ThrowsCryptographicException()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is available only on Windows.");
        File.WriteAllBytes(KeyFile, new byte[64]);

        // Act
        void Act() => new WindowsSnapshotKeyStore().Load(KeyFile);

        // Assert
        Assert.ThrowsAny<CryptographicException>(Act);
    }

    [Fact]
    public void Configure_WritesTheStartupEntryToADailyFileInTheGivenFolder()
    {
        // Arrange
        var previous = Log.Logger;
        var logs = Path.Combine(directory, "log");

        // Act
        try
        {
            ApplicationLogging.Configure(logs);
            Log.Information("Synthetic test entry");
            Log.CloseAndFlush();
        }
        finally
        {
            Log.Logger = previous;
        }

        // Assert
        var file = Assert.Single(Directory.GetFiles(logs, "balancia-*.log"));
        var text = File.ReadAllText(file);
        Assert.Contains("Application starting", text);
        Assert.Contains("Synthetic test entry", text);
        Assert.Matches(@"balancia-\d{8}\.log$", file);
    }

    public void Dispose() => Directory.Delete(directory, true);
}
