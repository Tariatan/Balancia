using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotEncryptionTests : IDisposable
{
    private const string Passphrase = "synthetic encryption test passphrase";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-encryption-" + Guid.NewGuid().ToString("N"));
    private readonly SnapshotKey key = SnapshotKey.Create(Passphrase);
    private readonly LedgerStore store;
    private string SnapshotPath => Path.Combine(directory, "Snapshot.balancia");

    public SnapshotEncryptionTests()
    {
        Directory.CreateDirectory(directory);
        store = new LedgerStore(Path.Combine(directory, "ledger.db"));
        store.Initialize();
        store.SaveAccount(null, "Synthetic account", new DateOnly(2026, 1, 1), new Money(12000));
    }

    [Fact]
    public void PassphraseDerivedOnAnotherDeviceUnlocksWholeArchive()
    {
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        var info = SnapshotEncryption.ReadInfo(SnapshotPath)!;
        using var portableKey = SnapshotKey.Derive(Passphrase, info);

        Assert.Equal(manifest, store.ValidateSnapshot(SnapshotPath, portableKey));
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(SnapshotPath));
        Assert.DoesNotContain(manifest.DatasetId, text);
        Assert.DoesNotContain("ledger.db", text);
        Assert.DoesNotContain("manifest.json", text);
        Assert.DoesNotContain(Passphrase, text);
    }

    [Fact]
    public void EachExportUsesFreshNonceEvenWithRememberedKey()
    {
        store.ExportSnapshot(SnapshotPath, key);
        var first = File.ReadAllBytes(SnapshotPath);
        store.ExportSnapshot(SnapshotPath, key);
        var second = File.ReadAllBytes(SnapshotPath);

        Assert.False(first.AsSpan(44, 12).SequenceEqual(second.AsSpan(44, 12)));
        Assert.Equal(key.Info, SnapshotEncryption.ReadInfo(SnapshotPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }

    [Fact]
    public void MissingOrWrongPassphraseLeavesLiveAndViewerCopiesUntouched()
    {
        store.ExportSnapshot(SnapshotPath, key);
        var viewerPath = Path.Combine(directory, "viewer.db");
        SnapshotImporter.Import(SnapshotPath, viewerPath, key);
        var liveHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "ledger.db")));
        var viewerHash = SHA256.HashData(File.ReadAllBytes(viewerPath));
        using var wrongKey = SnapshotKey.Derive("wrong synthetic passphrase", key.Info);

        Assert.Throws<InvalidOperationException>(() => store.RestoreSnapshot(SnapshotPath));
        Assert.Throws<InvalidDataException>(() => store.RestoreSnapshot(SnapshotPath, wrongKey));
        Assert.Throws<InvalidDataException>(() => SnapshotImporter.Import(SnapshotPath, viewerPath, wrongKey));
        Assert.Equal(liveHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "ledger.db"))));
        Assert.Equal(viewerHash, SHA256.HashData(File.ReadAllBytes(viewerPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
        Assert.Empty(Directory.GetFiles(directory, "*.pre-restore-*"));
    }

    [Theory]
    [InlineData(0)] // Unsupported version/magic.
    [InlineData(8)] // KDF parameters.
    [InlineData(44)] // Nonce is authenticated too.
    [InlineData(60)] // Tag.
    [InlineData(90)] // Ciphertext.
    public void TamperingIsRejectedBeforeReplacingViewer(int index)
    {
        store.ExportSnapshot(SnapshotPath, key);
        var viewerPath = Path.Combine(directory, "viewer.db");
        SnapshotImporter.Import(SnapshotPath, viewerPath, key);
        var hash = SHA256.HashData(File.ReadAllBytes(viewerPath));
        var data = File.ReadAllBytes(SnapshotPath);
        data[index] ^= 1;
        File.WriteAllBytes(SnapshotPath, data);

        if (index == 8)
        {
            Assert.Throws<InvalidOperationException>(() => SnapshotImporter.Import(SnapshotPath, viewerPath, key));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => SnapshotImporter.Import(SnapshotPath, viewerPath, key));
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(viewerPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
    }

    [Fact]
    public void TruncatedOrOversizedEnvelopeIsRejected()
    {
        store.ExportSnapshot(SnapshotPath, key);
        var data = File.ReadAllBytes(SnapshotPath);
        File.WriteAllBytes(SnapshotPath, data[..^1]);
        Assert.Throws<InvalidDataException>(() => store.ValidateSnapshot(SnapshotPath, key));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(56), int.MaxValue);
        File.WriteAllBytes(SnapshotPath, data);
        Assert.Throws<InvalidDataException>(() => SnapshotEncryption.ReadInfo(SnapshotPath));
    }

    [Fact]
    public void RememberedKeyRoundTripAndPassphraseChangePreserveOldFiles()
    {
        store.ExportSnapshot(SnapshotPath, key);
        var bytes = key.ExportSecret();
        using var remembered = SnapshotKey.ImportSecret(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        Assert.Equal(key.Info, remembered.Info);
        Assert.Equal(key.Info, SnapshotEncryption.ReadInfo(SnapshotPath));
        store.ValidateSnapshot(SnapshotPath, remembered);

        using var replacement = SnapshotKey.Create("new synthetic test passphrase");
        var newPath = Path.Combine(directory, "new.balancia");
        store.ExportSnapshot(newPath, replacement);
        using var old = SnapshotKey.Derive(Passphrase, SnapshotEncryption.ReadInfo(SnapshotPath)!);
        store.ValidateSnapshot(SnapshotPath, old);
        Assert.Throws<InvalidOperationException>(() => store.ValidateSnapshot(newPath, old));
    }

    [Fact]
    public void LegacyPlaintextSnapshotStillImportsAndRestores()
    {
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        var legacyPath = Path.Combine(directory, "legacy.balancia");
        using (var archive = ZipFile.Open(legacyPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(directory, "ledger.db"), "ledger.db");
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(manifest));
        }

        Assert.Null(SnapshotEncryption.ReadInfo(legacyPath));
        Assert.Equal(manifest, store.ValidateSnapshot(legacyPath));
        var (viewer, _) = SnapshotImporter.Import(legacyPath, Path.Combine(directory, "viewer.db"));
        Assert.Equal(12000, viewer.ReadSnapshot().NetWorth.Centimes);
        Assert.Equal(manifest, store.RestoreSnapshot(legacyPath).Manifest);
    }

    [Fact]
    public void InvalidEmbeddedDatabaseLeavesViewerUntouchedAndCleansStaging()
    {
        var manifest = store.ExportSnapshot(SnapshotPath, key);
        var viewerPath = Path.Combine(directory, "viewer.db");
        SnapshotImporter.Import(SnapshotPath, viewerPath, key);
        var previous = File.ReadAllBytes(viewerPath);
        var malformed = Path.Combine(directory, "malformed.balancia");
        using (var archive = ZipFile.Open(malformed, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest));
            }

            using var database = new StreamWriter(archive.CreateEntry("ledger.db").Open());
            database.Write("not a sqlite database");
        }

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => SnapshotImporter.Import(malformed, viewerPath));
        Assert.Equal(previous, File.ReadAllBytes(viewerPath));
        Assert.Empty(Directory.GetFiles(directory, "*.staged-*"));
    }

    [Fact]
    public void ExportCannotSilentlyFallBackToPlaintextOrReplacePreviousFile()
    {
        store.ExportSnapshot(SnapshotPath, key);
        var previous = File.ReadAllBytes(SnapshotPath);
        Assert.Throws<ArgumentNullException>(() => store.ExportSnapshot(SnapshotPath, null!));
        using var disposed = SnapshotKey.Create(Passphrase);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.ExportSnapshot(SnapshotPath, disposed));
        Assert.Equal(previous, File.ReadAllBytes(SnapshotPath));
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(directory, true);
    }
}
