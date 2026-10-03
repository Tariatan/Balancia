using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>Current-user DPAPI protection, without a plaintext key or password in settings.</summary>
internal static class WindowsSnapshotKeyStore
{
    internal static SnapshotKey? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (new FileInfo(path).Length > 16 * 1024)
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
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Remembering snapshot keys requires Windows.");
        }

        var input = new DataBlob
        {
            Size = bytes.Length,
            Data = Marshal.AllocHGlobal(bytes.Length)
        };
        DataBlob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            const uint noUi = 1;
            var succeeded = decrypt
                ? CryptUnprotectData(ref input, 0, 0, 0, 0, noUi, out output)
                : CryptProtectData(ref input, 0, 0, 0, 0, noUi, out output);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect or unlock the snapshot key.");
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Size; i++)
            {
                Marshal.WriteByte(input.Data, i, 0);
            }

            Marshal.FreeHGlobal(input.Data);
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
