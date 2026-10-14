using System.Security.Cryptography;
using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>Current-user DPAPI protection, without a plaintext key or password in settings.</summary>
internal static class WindowsSnapshotKeyStore
{
    private const int MaxProtectedKeyBytes = 16 * 1024;

    internal static SnapshotKey? Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        if (file.Length > MaxProtectedKeyBytes)
        {
            throw new InvalidDataException("The remembered snapshot key is invalid. Enter the passphrase again.");
        }

        var plaintext = Protect(File.ReadAllBytes(path), decrypt: true);
        try
        {
            return SnapshotKey.ImportSecret(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static void Save(string path, SnapshotKey key)
    {
        var secret = key.ExportSecret();
        byte[] protectedData;
        try
        {
            protectedData = Protect(secret, decrypt: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(protectedData);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Remembering snapshot keys requires Windows.");
        }

        return decrypt
            ? ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)
            : ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    }
}
