using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotEncryptionTests : IDisposable
{
    private const string Passphrase = "synthetic encryption test passphrase";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-encryption-" + Guid.NewGuid().ToString("N"));
    private readonly SnapshotKey key = SnapshotKey.Create(Passphrase);
    private readonly LedgerStore store;
    private string SnapshotPath => Path.Combine(directory, "Snapshot.balancia");
    private string LedgerPath => Path.Combine(directory, "ledger.db");
    private string ViewerPath => Path.Combine(directory, "viewer.db");

    public SnapshotEncryptionTests()
    {
        Directory.CreateDirectory(directory);
        store = new LedgerStore(LedgerPath);
        store.Initialize();
        store.SaveAccount(null, "Synthetic account", new DateOnly(2026, 1, 1), new Money(12000));
    }

    [Fact]
    public void ValidateSnapshot_KeyDerivedFromPassphraseAndInfo_UnlocksArchive()
    {
        // Arrange
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        using var portableKey = SnapshotKey.Derive(Passphrase, SnapshotEncryption.ReadInfo(SnapshotPath));

        // Act
        var validated = store.ValidateSnapshot(SnapshotPath, portableKey);

        // Assert
        Assert.Equal(manifest, validated);
    }

    [Fact]
    public void ExportSnapshot_ValidLedger_HidesContentsAndPassphraseInEnvelope()
    {
        // Arrange
        var manifest = store.ExportSnapshot(SnapshotPath, key);

        // Act
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(SnapshotPath));

        // Assert
        Assert.DoesNotContain(manifest.DatasetId, text);
        Assert.DoesNotContain("ledger.db", text);
        Assert.DoesNotContain("manifest.json", text);
        Assert.DoesNotContain(Passphrase, text);
    }

    [Fact]
    public void ExportSnapshot_RepeatedWithRememberedKey_UsesFreshNonceAndKeepsKeyInfo()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var first = File.ReadAllBytes(SnapshotPath);

        // Act
        store.ExportSnapshot(SnapshotPath, key);
        var second = File.ReadAllBytes(SnapshotPath);

        // Assert
        Assert.False(first.AsSpan(44, 12).SequenceEqual(second.AsSpan(44, 12)));
        Assert.Equal(key.Info, SnapshotEncryption.ReadInfo(SnapshotPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void RestoreSnapshot_WrongOrUnrelatedKey_LeavesLiveLedgerUntouched()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var liveHash = SHA256.HashData(File.ReadAllBytes(LedgerPath));
        using var wrongKey = SnapshotKey.Derive("wrong synthetic passphrase", key.Info);
        using var otherKey = SnapshotKey.Create("other synthetic passphrase");

        // Act
        var otherKeyError = Record.Exception(() => store.RestoreSnapshot(SnapshotPath, otherKey));
        var wrongKeyError = Record.Exception(() => store.RestoreSnapshot(SnapshotPath, wrongKey));

        // Assert
        Assert.IsType<InvalidOperationException>(otherKeyError);
        Assert.IsType<SnapshotAuthenticationException>(wrongKeyError);
        Assert.Equal(liveHash, SHA256.HashData(File.ReadAllBytes(LedgerPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
        Assert.Empty(Directory.GetFiles(directory, "*.pre-restore-*"));
    }

    [Fact]
    public void Import_WrongKey_ThrowsAndLeavesViewerCopyUntouched()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        SnapshotImporter.Import(SnapshotPath, ViewerPath, key);
        var viewerHash = SHA256.HashData(File.ReadAllBytes(ViewerPath));
        using var wrongKey = SnapshotKey.Derive("wrong synthetic passphrase", key.Info);

        // Act
        void Act() => SnapshotImporter.Import(SnapshotPath, ViewerPath, wrongKey);

        // Assert
        Assert.Throws<SnapshotAuthenticationException>(Act);
        Assert.Equal(viewerHash, SHA256.HashData(File.ReadAllBytes(ViewerPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
    }

    [Theory]
    [InlineData(0, typeof(InvalidDataException))] // Unsupported version/magic.
    [InlineData(8, typeof(InvalidOperationException))] // KDF parameters.
    [InlineData(44, typeof(SnapshotAuthenticationException))] // Nonce is authenticated too.
    [InlineData(60, typeof(SnapshotAuthenticationException))] // Tag.
    [InlineData(90, typeof(SnapshotAuthenticationException))] // Ciphertext.
    public void Import_TamperedByte_ThrowsBeforeReplacingViewer(int index, Type expectedException)
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        SnapshotImporter.Import(SnapshotPath, ViewerPath, key);
        var viewerHash = SHA256.HashData(File.ReadAllBytes(ViewerPath));
        var data = File.ReadAllBytes(SnapshotPath);
        data[index] ^= 1;
        File.WriteAllBytes(SnapshotPath, data);

        // Act
        var error = Record.Exception(() => SnapshotImporter.Import(SnapshotPath, ViewerPath, key));

        // Assert
        Assert.IsType(expectedException, error);
        Assert.Equal(viewerHash, SHA256.HashData(File.ReadAllBytes(ViewerPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
    }

    [Fact]
    public void ValidateSnapshot_TruncatedEnvelope_ThrowsInvalidDataException()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var data = File.ReadAllBytes(SnapshotPath);
        File.WriteAllBytes(SnapshotPath, data[..^1]);

        // Act
        void Act() => store.ValidateSnapshot(SnapshotPath, key);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void ReadInfo_OversizedCiphertextLength_ThrowsInvalidDataException()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var data = File.ReadAllBytes(SnapshotPath);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(56), int.MaxValue);
        File.WriteAllBytes(SnapshotPath, data);

        // Act
        void Act() => SnapshotEncryption.ReadInfo(SnapshotPath);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void ImportSecret_ExportedRememberedKey_ReproducesKeyInfoAndValidatesSnapshot()
    {
        // Arrange
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        var bytes = key.ExportSecret();
        using var remembered = SnapshotKey.ImportSecret(bytes);
        CryptographicOperations.ZeroMemory(bytes);

        // Act
        var validated = store.ValidateSnapshot(SnapshotPath, remembered);

        // Assert
        Assert.Equal(key.Info, remembered.Info);
        Assert.Equal(manifest, validated);
    }

    [Fact]
    public void ValidateSnapshot_AfterPassphraseChange_OldKeyOpensOldFileButNotNewFile()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        using var replacement = SnapshotKey.Create("new synthetic test passphrase");
        var newPath = Path.Combine(directory, "new.balancia");
        store.ExportSnapshot(newPath, replacement);
        using var old = SnapshotKey.Derive(Passphrase, SnapshotEncryption.ReadInfo(SnapshotPath));

        // Act
        var oldFileManifest = store.ValidateSnapshot(SnapshotPath, old);
        var newFileError = Record.Exception(() => store.ValidateSnapshot(newPath, old));

        // Assert
        Assert.NotNull(oldFileManifest);
        Assert.IsType<InvalidOperationException>(newFileError);
    }

    [Fact]
    public void ReadInfo_PlaintextZipSnapshot_ThrowsInvalidDataException()
    {
        // Arrange
        var plaintextPath = CreatePlaintextSnapshot();

        // Act
        void Act() => SnapshotEncryption.ReadInfo(plaintextPath);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void ValidateSnapshot_PlaintextZipSnapshot_ThrowsInvalidDataException()
    {
        // Arrange
        var plaintextPath = CreatePlaintextSnapshot();

        // Act
        void Act() => store.ValidateSnapshot(plaintextPath, key);

        // Assert
        Assert.Throws<InvalidDataException>(Act);
    }

    [Fact]
    public void Import_EmbeddedDatabaseIsNotSqlite_ThrowsLeavesViewerUntouchedAndCleansStaging()
    {
        // Arrange
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        SnapshotImporter.Import(SnapshotPath, ViewerPath, key);
        var previous = File.ReadAllBytes(ViewerPath);
        var malformed = Path.Combine(directory, "malformed.balancia");
        EncryptedArchive.Write(malformed, key, archive =>
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest));
            }

            using var database = new StreamWriter(archive.CreateEntry("ledger.db").Open());
            database.Write("not a sqlite database");
        });

        // Act
        void Act() => SnapshotImporter.Import(malformed, ViewerPath, key);

        // Assert
        Assert.Throws<SqliteException>(Act);
        Assert.Equal(previous, File.ReadAllBytes(ViewerPath));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
    }

    [Fact]
    public void ExportSnapshot_NullKey_ThrowsAndKeepsPreviousFile()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var previous = File.ReadAllBytes(SnapshotPath);

        // Act
        void Act() => store.ExportSnapshot(SnapshotPath, null!);

        // Assert
        Assert.Throws<ArgumentNullException>(Act);
        Assert.Equal(previous, File.ReadAllBytes(SnapshotPath));
    }

    [Fact]
    public void ExportSnapshot_DisposedKey_ThrowsAndKeepsPreviousFile()
    {
        // Arrange
        store.ExportSnapshot(SnapshotPath, key);
        var previous = File.ReadAllBytes(SnapshotPath);
        var disposed = SnapshotKey.Create(Passphrase);
        disposed.Dispose();

        // Act
        void Act() => store.ExportSnapshot(SnapshotPath, disposed);

        // Assert
        Assert.Throws<ObjectDisposedException>(Act);
        Assert.Equal(previous, File.ReadAllBytes(SnapshotPath));
    }

    private string CreatePlaintextSnapshot()
    {
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        var plaintextPath = Path.Combine(directory, "plaintext.balancia");
        using var archive = ZipFile.Open(plaintextPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(LedgerPath, "ledger.db");
        using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        writer.Write(JsonSerializer.Serialize(manifest));
        return plaintextPath;
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(directory, true);
    }
}
