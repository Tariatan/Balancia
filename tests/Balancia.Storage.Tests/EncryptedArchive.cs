using System.IO.Compression;

namespace Balancia.Storage.Tests;

internal static class EncryptedArchive
{
    /// <summary>Writes a snapshot envelope around a hand-built ZIP so tests can exercise content validation.</summary>
    public static void Write(string path, SnapshotKey key, Action<ZipArchive> fill)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            fill(archive);
        }

        using var output = File.Create(path);
        SnapshotEncryption.Write(output, buffer.GetBuffer().AsSpan(0, (int)buffer.Length), key);
    }
}
