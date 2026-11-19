using Android.Content;
using Android.Provider;
using Serilog;
using System.Globalization;
using System.IO.Compression;
using AndroidEnvironment = Android.OS.Environment;

namespace Balancia.Android;

internal static class LogArchiveExporter
{
    internal static string? Export(Context context)
    {
        var directory = new DirectoryInfo(ApplicationLogging.GetLogDirectory(context));
        var files = directory.Exists ? directory.GetFiles("*.log") : [];
        if (files.Length == 0)
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        var fileName = $"Balancia-logs-{timestamp}.zip";
        Log.Information("Starting Android log export, Archive: '{Archive}', FileCount: '{FileCount}'", fileName, files.Length);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            SaveWithMediaStore(context, fileName, files);
        }
        else
        {
            SaveToPublicDownload(fileName, files);
        }

        Log.Information("Android log export completed, Archive: '{Archive}', FileCount: '{FileCount}'", fileName, files.Length);
        return fileName;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static void SaveWithMediaStore(Context context, string fileName, FileInfo[] files)
    {
        var resolver = context.ContentResolver ?? throw new IOException("The Downloads provider is unavailable.");
        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "application/zip");
        values.Put(MediaStore.IMediaColumns.RelativePath, AndroidEnvironment.DirectoryDownloads);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        using var collection = MediaStore.Downloads.GetContentUri(MediaStore.VolumeExternalPrimary);
        using var location = resolver.Insert(collection!, values) ?? throw new IOException("Could not create a Downloads entry.");

        try
        {
            WriteArchive(resolver.OpenOutputStream(location) ?? throw new IOException("Could not open the Downloads entry."), files);
            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            if (resolver.Update(location, values, null, null) != 1)
            {
                throw new IOException("Could not publish the Downloads entry.");
            }
        }
        catch
        {
            try
            {
                resolver.Delete(location, null, null);
            }
            catch (Java.Lang.Exception ex)
            {
                Log.Warning("Pending Android log export cleanup failed, FailureType: '{FailureType}'", ex.GetType().Name);
            }

            throw;
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("android29.0")]
    private static void SaveToPublicDownload(string fileName, FileInfo[] files)
    {
        using var downloads = AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryDownloads);
        var directory = downloads?.AbsolutePath ?? throw new IOException("The Download directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{fileName}.partial");
        var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
        try
        {
            WriteArchive(output, files);
            File.Move(temporaryPath, Path.Combine(directory, fileName));
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Temporary Android log export cleanup failed, FailureType: '{FailureType}'", ex.GetType().Name);
            }

            throw;
        }
    }

    private static void WriteArchive(Stream destination, FileInfo[] files)
    {
        using var output = destination;
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            using var input = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var entry = archive.CreateEntry(file.Name).Open();
            input.CopyTo(entry);
        }
    }
}
