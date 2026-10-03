using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Balancia.Storage;

public sealed record SnapshotEncryptionInfo(int Iterations, string Salt);

/// <summary>A derived snapshot key. Persist ExportSecret() only through platform-protected storage.</summary>
public sealed class SnapshotKey : IDisposable
{
    private readonly byte[] secret;
    public SnapshotEncryptionInfo Info { get; }
    internal ReadOnlySpan<byte> Secret => secret;

    private SnapshotKey(SnapshotEncryptionInfo info, byte[] secret)
    {
        Info = info;
        this.secret = secret;
    }

    public static SnapshotKey Create(string passphrase) => Derive(passphrase,
        new SnapshotEncryptionInfo(600_000, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public static SnapshotKey Derive(string passphrase, SnapshotEncryptionInfo info)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        ValidateInfo(info);
        return new SnapshotKey(info, Rfc2898DeriveBytes.Pbkdf2(passphrase, Convert.FromBase64String(info.Salt),
            info.Iterations, HashAlgorithmName.SHA256, 32));
    }

    public bool Matches(SnapshotEncryptionInfo info) => Info == info;

    public byte[] ExportSecret()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = new byte[68];
        BinaryPrimitives.WriteInt32LittleEndian(result, Info.Iterations);
        Convert.FromBase64String(Info.Salt).CopyTo(result, 4);
        secret.CopyTo(result, 36);
        return result;
    }

    public static SnapshotKey ImportSecret(byte[] data)
    {
        if (data.Length != 68)
        {
            throw new InvalidDataException("The remembered snapshot key is invalid. Enter the passphrase again.");
        }

        var info = new SnapshotEncryptionInfo(BinaryPrimitives.ReadInt32LittleEndian(data),
            Convert.ToBase64String(data.AsSpan(4, 32)));
        ValidateInfo(info);
        return new SnapshotKey(info, data.AsSpan(36, 32).ToArray());
    }

    internal static void ValidateInfo(SnapshotEncryptionInfo info)
    {
        if (info.Iterations is < 600_000 or > 2_000_000 || Convert.FromBase64String(info.Salt).Length != 32)
        {
            throw new InvalidDataException("Snapshot encryption parameters are unsupported.");
        }
    }

    private bool disposed;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(secret);
        disposed = true;
    }

    internal void CheckUsable() => ObjectDisposedException.ThrowIf(disposed, this);
}
