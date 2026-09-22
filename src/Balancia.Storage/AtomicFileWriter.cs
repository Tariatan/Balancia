namespace Balancia.Storage;

internal static class AtomicFileWriter
{
    /// <summary>Writes to a sibling temp file, then atomically replaces <paramref name="destination"/>; the temp file is removed if <paramref name="write"/> or the move throws.</summary>
    public static void Write(string destination, Action<Stream> write)
    {
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
