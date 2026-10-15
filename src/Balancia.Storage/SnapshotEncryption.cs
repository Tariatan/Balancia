using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Serilog;

namespace Balancia.Storage;

/// <summary>Version 1: authenticated header, PBKDF2-SHA256, AES-256-GCM; the entire ZIP is encrypted.</summary>
public static class SnapshotEncryption
{
    private static ILogger Logger => Log.ForContext(typeof(SnapshotEncryption));
    private static ReadOnlySpan<byte> Magic => "BALENC01"u8;
    private const int MagicSize = 8;
    private const int IterationsOffset = 8;
    private const int SaltOffset = 12;
    private const int SaltSize = SnapshotKey.SaltSize;
    private const int NonceOffset = 44;
    private const int NonceSize = 12;
    private const int LengthOffset = 56;
    private const int HeaderSize = 60;
    private const int TagSize = 16;
    private const int MaximumArchiveSize = 256 * 1024 * 1024;

    private readonly record struct Header(byte[] Bytes, SnapshotEncryptionInfo Info, int Length)
    {
        public ReadOnlySpan<byte> Nonce => Bytes.AsSpan(NonceOffset, NonceSize);
    }

    public static SnapshotEncryptionInfo ReadInfo(string source)
    {
        using var stream = File.OpenRead(source);
        return ReadHeader(stream).Info;
    }

    private static Header ReadHeader(Stream stream)
    {
        var header = new byte[HeaderSize];
        ReadOrThrow(stream, header.AsSpan(0, MagicSize));
        if (!header.AsSpan(0, MagicSize).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Snapshot encryption version or format is unsupported.");
        }

        ReadOrThrow(stream, header.AsSpan(MagicSize));
        var info = new SnapshotEncryptionInfo(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(IterationsOffset)),
            Convert.ToBase64String(header.AsSpan(SaltOffset, SaltSize)));
        SnapshotKey.ValidateInfo(info);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(LengthOffset));
        if (length <= 0 || length > MaximumArchiveSize || stream.Length != HeaderSize + TagSize + (long)length)
        {
            throw new InvalidDataException("Snapshot is incomplete or exceeds the supported size.");
        }

        return new Header(header, info, length);
    }

    private static void ReadOrThrow(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Snapshot is incomplete or unsupported.");
        }
    }

    internal static ZipArchive OpenArchive(string source, SnapshotKey key)
    {
        using var stream = File.OpenRead(source);
        var header = ReadHeader(stream);
        if (!key.Matches(header.Info))
        {
            Logger.Information("Snapshot unlock requires a matching key");
            throw new InvalidOperationException("Enter the snapshot passphrase to unlock this file.");
        }

        key.CheckUsable();
        var tag = new byte[TagSize];
        var ciphertext = new byte[header.Length];
        var plaintext = new byte[header.Length];
        stream.ReadExactly(tag);
        stream.ReadExactly(ciphertext);
        try
        {
            using var aes = new AesGcm(key.Secret, TagSize);
            aes.Decrypt(header.Nonce, ciphertext, tag, plaintext, header.Bytes);
            Logger.Information("Encrypted snapshot authenticated and decrypted");
            return new ZipArchive(new ClearingMemoryStream(plaintext), ZipArchiveMode.Read);
        }
        catch (CryptographicException)
        {
            Logger.Information("Snapshot authentication rejected; incorrect passphrase or damaged file");
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
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(IterationsOffset), key.Info.Iterations);
        Convert.FromBase64String(key.Info.Salt).CopyTo(header, SaltOffset);
        var nonce = header.AsSpan(NonceOffset, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(LengthOffset), archive.Length);
        var tag = new byte[TagSize];
        var ciphertext = new byte[archive.Length];
        using var aes = new AesGcm(key.Secret, TagSize);
        aes.Encrypt(nonce, archive, ciphertext, tag, header);
        destination.Write(header);
        destination.Write(tag);
        destination.Write(ciphertext);
    }

    private sealed class ClearingMemoryStream(byte[] plaintext) : MemoryStream(plaintext, writable: false)
    {
        private readonly byte[] plaintext = plaintext;

        protected override void Dispose(bool disposing)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            base.Dispose(disposing);
        }
    }
}
