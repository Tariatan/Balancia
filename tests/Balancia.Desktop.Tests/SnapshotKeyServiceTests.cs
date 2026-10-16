using System.ComponentModel;
using System.Security.Cryptography;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class SnapshotKeyServiceTests
{
    private static SnapshotKeyService Service(FakeKeyStore store) => new(store, () => "key.dpapi");

    [Fact]
    public void Load_NothingRemembered_ReportsMissing()
    {
        // Arrange
        using var service = Service(new FakeKeyStore());

        // Act
        var result = service.Load();

        // Assert
        Assert.Equal(KeyLoadResult.Missing, result);
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void Load_RememberedKey_IsUsed()
    {
        // Arrange
        using var service = Service(new FakeKeyStore { Stored = DesktopWorkspace.CreateKey() });

        // Act
        var result = service.Load();

        // Assert
        Assert.Equal(KeyLoadResult.Loaded, result);
        Assert.True(service.IsConfigured);
    }

    [Theory]
    [InlineData("crypto")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("data")]
    [InlineData("win32")]
    public void Load_ProtectedStorageFailure_ReportsUnavailableInsteadOfThrowing(string failure)
    {
        // Arrange
        Exception exception = failure switch
        {
            "crypto" => new CryptographicException("The data is invalid."),
            "io" => new IOException("locked"),
            "access" => new UnauthorizedAccessException(),
            "data" => new InvalidDataException("bad"),
            _ => new Win32Exception(5),
        };
        using var service = Service(new FakeKeyStore { LoadFailure = exception });

        // Act
        var result = service.Load();

        // Assert
        Assert.Equal(KeyLoadResult.Unavailable, result);
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void Load_UnexpectedFailure_Propagates()
    {
        // Arrange
        using var service = Service(new FakeKeyStore { LoadFailure = new NotSupportedException() });

        // Act
        void Act() => service.Load();

        // Assert
        Assert.Throws<NotSupportedException>(Act);
    }

    [Fact]
    public void Load_KeyAlreadyInUse_DoesNotTouchTheStore()
    {
        // Arrange
        using var service = Service(new FakeKeyStore { LoadFailure = new IOException() });
        service.Use(DesktopWorkspace.CreateKey());

        // Act
        var result = service.Load();

        // Assert
        Assert.Equal(KeyLoadResult.Loaded, result);
    }

    [Fact]
    public void Replace_SavesThenSwitchesAndDisposesThePreviousKey()
    {
        // Arrange
        var store = new FakeKeyStore();
        using var service = Service(store);
        var previous = DesktopWorkspace.CreateKey();
        service.Use(previous);
        var replacement = SnapshotKey.Create("synthetic replacement passphrase");

        // Act
        service.Replace(replacement);

        // Assert
        Assert.Same(replacement, service.Key);
        Assert.Equal(1, store.SaveCount);
        Assert.Throws<ObjectDisposedException>(previous.ExportSecret);
    }

    [Fact]
    public void Replace_SaveFails_KeepsThePreviousKeyUsable()
    {
        // Arrange
        var store = new FakeKeyStore { SaveFailure = new IOException("disk full") };
        using var service = Service(store);
        var previous = DesktopWorkspace.CreateKey();
        service.Use(previous);
        using var replacement = SnapshotKey.Create("synthetic replacement passphrase");

        // Act
        void Act() => service.Replace(replacement);

        // Assert
        Assert.Throws<IOException>(Act);
        Assert.Same(previous, service.Key);
        Assert.Equal(68, previous.ExportSecret().Length);
    }

    [Fact]
    public void Dispose_ClearsAndDisposesTheKey()
    {
        // Arrange
        var service = Service(new FakeKeyStore());
        var key = DesktopWorkspace.CreateKey();
        service.Use(key);

        // Act
        service.Dispose();

        // Assert
        Assert.Null(service.Key);
        Assert.Throws<ObjectDisposedException>(key.ExportSecret);
    }
}
