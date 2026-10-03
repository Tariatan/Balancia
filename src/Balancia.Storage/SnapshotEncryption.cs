using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Serilog;

namespace Balancia.Storage;

/// <summary>Version 1: authenticated header, PBKDF2-SHA256, AES-256-GCM; the entire ZIP is encrypted.</summary>
public static class SnapshotEncryption
{
    private static ReadOnlySpan<byte> Magic => "BALENC01"u8;
    private const int HeaderSize = 60;
    private const int TagSize = 16;
    internal const int MaximumArchiveSize = 256 * 1024 * 1024;

    public static SnapshotEncryptionInfo? ReadInfo(string source)
    {
        using var stream = File.OpenRead(source);
        return ReadHeader(stream)?.Info;
    }

    private static (byte[] Bytes, SnapshotEncryptionInfo Info)? ReadHeader(Stream stream)
    {
        var prefix = new byte[8];
        try
        {
            stream.ReadExactly(prefix);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Snapshot is incomplete or unsupported.");
        }

        if (prefix.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8))
        {
            return null; // Older plaintext snapshots remain importable.
        }

        if (!prefix.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Snapshot encryption version or format is unsupported.");
        }

        var header = new byte[HeaderSize];
        prefix.CopyTo(header, 0);
        try
        {
            stream.ReadExactly(header.AsSpan(8));
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Snapshot is incomplete or unsupported.");
        }

        var info = new SnapshotEncryptionInfo(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)),
            Convert.ToBase64String(header.AsSpan(12, 32)));
        SnapshotKey.ValidateInfo(info);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(56));
        if (length <= 0 || length > MaximumArchiveSize || stream.Length != HeaderSize + TagSize + (long)length)
        {
            throw new InvalidDataException("Snapshot is incomplete or exceeds the supported size.");
        }

        return (header, info);
    }

    internal static ZipArchive OpenArchive(string source, SnapshotKey? key)
    {
        using var stream = File.OpenRead(source);
        var header = ReadHeader(stream);
        if (header is null)
        {
            if (stream.Length > MaximumArchiveSize)
            {
                throw new InvalidDataException("Snapshot exceeds the supported size.");
            }

            Log.ForContext(typeof(SnapshotEncryption)).Information("Opening legacy plaintext snapshot");
            return ZipFile.OpenRead(source);
        }

        if (key is null || !key.Matches(header.Value.Info))
        {
            Log.ForContext(typeof(SnapshotEncryption)).Information("Snapshot unlock requires a matching key");
            throw new InvalidOperationException("Enter the snapshot passphrase to unlock this file.");
        }

        key.CheckUsable();
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.Value.Bytes.AsSpan(56));
        var tag = new byte[TagSize];
        var ciphertext = new byte[length];
        var plaintext = new byte[length];
        stream.ReadExactly(tag);
        stream.ReadExactly(ciphertext);
        try
        {
            using var aes = new AesGcm(key.Secret, TagSize);
            aes.Decrypt(header.Value.Bytes.AsSpan(44, 12), ciphertext, tag, plaintext, header.Value.Bytes);
            Log.ForContext(typeof(SnapshotEncryption)).Information("Encrypted snapshot authenticated and decrypted");
            return new ZipArchive(new ClearingMemoryStream(plaintext), ZipArchiveMode.Read);
        }
        catch (CryptographicException)
        {
            Log.ForContext(typeof(SnapshotEncryption)).Information("Snapshot authentication rejected; incorrect passphrase or damaged file");
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("The snapshot passphrase is incorrect or the file is damaged.");
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    internal static void Write(Stream destination, ReadOnlySpan<byte> archive, SnapshotKey key)
    {
        key.CheckUsable();
        if (archive.Length > MaximumArchiveSize)
        {
            throw new InvalidDataException("Snapshot exceeds the supported size.");
        }

        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), key.Info.Iterations);
        Convert.FromBase64String(key.Info.Salt).CopyTo(header, 12);
        RandomNumberGenerator.Fill(header.AsSpan(44, 12));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(56), archive.Length);
        var tag = new byte[TagSize];
        var ciphertext = new byte[archive.Length];
        using var aes = new AesGcm(key.Secret, TagSize);
        aes.Encrypt(header.AsSpan(44, 12), archive, ciphertext, tag, header);
        destination.Write(header);
        destination.Write(tag);
        destination.Write(ciphertext);
    }

    private sealed class ClearingMemoryStream : MemoryStream
    {
        private readonly byte[] plaintext;

        public ClearingMemoryStream(byte[] plaintext) : base(plaintext, writable: false)
        {
            this.plaintext = plaintext;
        }

        protected override void Dispose(bool disposing)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            base.Dispose(disposing);
        }
    }
}
