using Serilog;

namespace Balancia.Storage;

public enum ViewerImportOutcome
{
    Imported,

    /// <summary>The snapshot is in place but the key could not be remembered, so the next import asks again.</summary>
    ImportedKeyNotRemembered,

    /// <summary>The user declined to enter the passphrase; the previous copy is unchanged.</summary>
    Canceled,
}

/// <summary>What a viewer platform supplies to <see cref="ViewerSnapshotImport"/>: protected key storage, the passphrase prompt, and the display of an imported copy.</summary>
public interface IViewerSnapshotHost : ISnapshotUnlockHost
{
    void RememberKey(SnapshotKey key);

    /// <summary>Called once the snapshot replaced the viewer copy, before the key is remembered. Runs on the caller's context.</summary>
    Task ImportedAsync(LedgerReader reader, SnapshotManifest manifest);
}

/// <summary>
/// The viewer's whole import sequence: unlock the snapshot with <see cref="SnapshotUnlock"/>, validate and swap it in, then remember the key.
/// Heavy work runs off the caller's context; host callbacks run on it. Any failure other than a wrong passphrase propagates with the
/// previous copy untouched.
/// </summary>
public static class ViewerSnapshotImport
{
    public static async Task<ViewerImportOutcome> RunAsync(string archivePath, string dbPath, IViewerSnapshotHost host)
    {
        var logger = Log.ForContext(typeof(ViewerSnapshotImport));
        var result = await SnapshotUnlock.RunAsync(archivePath, host, async key =>
        {
            var (reader, manifest) = await Task.Run(() => SnapshotImporter.Import(archivePath, dbPath, key));
            await host.ImportedAsync(reader, manifest);
            try
            {
                await Task.Run(() => host.RememberKey(key));
                logger.Information("Viewer snapshot key remembered");
                return ViewerImportOutcome.Imported;
            }
            catch (Exception ex)
            {
                logger.Warning("Viewer snapshot loaded but key could not be remembered, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                return ViewerImportOutcome.ImportedKeyNotRemembered;
            }
        });
        return result.Canceled ? ViewerImportOutcome.Canceled : result.Value;
    }
}
