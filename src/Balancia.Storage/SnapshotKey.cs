using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Balancia.Storage;

public sealed record SnapshotEncryptionInfo(int Iterations, string Salt);

/// <summary>A derived snapshot key. Persist ExportSecret() only through platform-protected storage.</summary>
public sealed class SnapshotKey : IDisposable
{
    private const int MinIterations = 600_000;
    private const int MaxIterations = 2_000_000;
    internal const int SaltSize = 32;
    private const int SecretSize = 32;
    private const int SaltOffset = sizeof(int);
    private const int SecretOffset = SaltOffset + SaltSize;
    private const int ExportSize = SecretOffset + SecretSize;

    private readonly byte[] secret;
    private bool disposed;

    public SnapshotEncryptionInfo Info { get; }
    internal ReadOnlySpan<byte> Secret => secret;

    private SnapshotKey(SnapshotEncryptionInfo info, byte[] secret)
    {
        Info = info;
        this.secret = secret;
    }

    public static SnapshotKey Create(string passphrase) => Derive(passphrase,
        new SnapshotEncryptionInfo(MinIterations, Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize))));

    public static SnapshotKey Derive(string passphrase, SnapshotEncryptionInfo info)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        var salt = DecodeSalt(info);
        return new SnapshotKey(info,
            Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, info.Iterations, HashAlgorithmName.SHA256, SecretSize));
    }

    public bool Matches(SnapshotEncryptionInfo info) => Info == info;

    public byte[] ExportSecret()
    {
        CheckUsable();
        var result = new byte[ExportSize];
        BinaryPrimitives.WriteInt32LittleEndian(result, Info.Iterations);
        DecodeSalt(Info).CopyTo(result, SaltOffset);
        secret.CopyTo(result, SecretOffset);
        return result;
    }

    public static SnapshotKey ImportSecret(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != ExportSize)
        {
            throw new InvalidDataException("The remembered snapshot key is invalid. Enter the passphrase again.");
        }

        var info = new SnapshotEncryptionInfo(BinaryPrimitives.ReadInt32LittleEndian(data),
            Convert.ToBase64String(data.AsSpan(SaltOffset, SaltSize)));
        ValidateInfo(info);
        return new SnapshotKey(info, [.. data.AsSpan(SecretOffset, SecretSize)]);
    }

    internal static void ValidateInfo(SnapshotEncryptionInfo info) => DecodeSalt(info);

    /// <summary>Validates the parameters and returns the decoded salt; malformed input is reported as InvalidDataException.</summary>
    private static byte[] DecodeSalt(SnapshotEncryptionInfo info)
    {
        Span<byte> salt = stackalloc byte[SaltSize];
        if (info.Iterations is < MinIterations or > MaxIterations
            || !Convert.TryFromBase64String(info.Salt, salt, out var written)
            || written != SaltSize)
        {
            throw new InvalidDataException("Snapshot encryption parameters are unsupported.");
        }

        return [.. salt];
    }

    public void Dispose()
    {
        disposed = true;
        CryptographicOperations.ZeroMemory(secret);
    }

    internal void CheckUsable() => ObjectDisposedException.ThrowIf(disposed, this);
}
