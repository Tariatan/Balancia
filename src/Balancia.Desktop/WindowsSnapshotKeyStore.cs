using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>Current-user DPAPI protection, without a plaintext key or password in settings.</summary>
internal static class WindowsSnapshotKeyStore
{
    private const int MaxProtectedKeyBytes = 16 * 1024;
    private const uint CryptProtectUiForbidden = 1;

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

        // Pinning avoids a second unmanaged copy of the input; callers zero their own buffers.
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        DataBlob output = default;
        try
        {
            var input = new DataBlob { Size = bytes.Length, Data = pinned.AddrOfPinnedObject() };
            var succeeded = decrypt
                ? CryptUnprotectData(ref input, 0, 0, 0, 0, CryptProtectUiForbidden, out output)
                : CryptProtectData(ref input, 0, 0, 0, 0, CryptProtectUiForbidden, out output);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not protect or unlock the snapshot key.");
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            pinned.Free();
            if (output.Data != 0)
            {
                for (var i = 0; i < output.Size; i++)
                {
                    Marshal.WriteByte(output.Data, i, 0);
                }

                _ = LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, nint description, nint entropy, nint reserved,
        nint prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved,
        nint prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
