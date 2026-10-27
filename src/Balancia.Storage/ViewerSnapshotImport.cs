using Serilog;

namespace Balancia.Storage;

/// <summary>Why the viewer needs the passphrase typed in.</summary>
public enum PassphraseReason
{
    /// <summary>No remembered key matches this snapshot (first import, or a changed passphrase).</summary>
    Pairing,

    /// <summary>Loading the remembered key failed (reinstall, key invalidation, damaged cache).</summary>
    RememberedKeyUnavailable,

    /// <summary>The previous passphrase did not unlock the snapshot.</summary>
    WrongPassphrase,
}

public enum ViewerImportOutcome
{
    Imported,

    /// <summary>The snapshot is in place but the key could not be remembered, so the next import asks again.</summary>
    ImportedKeyNotRemembered,

    /// <summary>The user declined to enter the passphrase; the previous copy is unchanged.</summary>
    Canceled,
}

/// <summary>What a viewer platform supplies to <see cref="ViewerSnapshotImport"/>: protected key storage, the passphrase prompt, and the display of an imported copy.</summary>
public interface IViewerSnapshotHost
{
    /// <summary>The remembered key, or null when none was saved; throws when it cannot be read. The caller owns and disposes the key.</summary>
    SnapshotKey? LoadRememberedKey();

    void RememberKey(SnapshotKey key);

    /// <summary>Asks for the passphrase; null when the user cancels. <paramref name="error"/> is the previous failure on a retry. Runs on the caller's context.</summary>
    Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error);

    /// <summary>Called once the snapshot replaced the viewer copy, before the key is remembered. Runs on the caller's context.</summary>
    Task ImportedAsync(LedgerReader reader, SnapshotManifest manifest);
}

/// <summary>
/// The viewer's whole import sequence: use the remembered key when it matches the file, otherwise ask for the passphrase (and again after a
/// wrong one), validate and swap the snapshot in, then remember the key. Heavy work runs off the caller's context; host callbacks run on it.
/// Any failure other than a wrong passphrase propagates with the previous copy untouched.
/// </summary>
public static class ViewerSnapshotImport
{
    public static async Task<ViewerImportOutcome> RunAsync(string archivePath, string dbPath, IViewerSnapshotHost host)
    {
        var logger = Log.ForContext(typeof(ViewerSnapshotImport));
        var info = await Task.Run(() => SnapshotEncryption.ReadInfo(archivePath));
        SnapshotKey? key = null;
        var reason = PassphraseReason.Pairing;
        string? unlockError = null;
        try
        {
            try
            {
                key = await Task.Run(host.LoadRememberedKey);
                logger.Information("Viewer remembered snapshot key loaded, KeyAvailable: '{KeyAvailable}'", key is not null);
            }
            catch (Exception ex)
            {
                logger.Warning("Viewer remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                // Reinstallation, key invalidation, or a damaged cache requires pairing again.
                reason = PassphraseReason.RememberedKeyUnavailable;
            }

            if (key is not null && !key.Matches(info))
            {
                logger.Information("Viewer remembered key does not match snapshot; pairing required");
                key.Dispose();
                key = null;
            }
            else if (key is not null)
            {
                logger.Information("Viewer snapshot unlock using remembered key");
            }

            while (true)
            {
                if (key is null)
                {
                    logger.Information("Viewer snapshot unlock prompting for passphrase");
                    var passphrase = await host.AskPassphraseAsync(reason, unlockError);
                    if (passphrase is null)
                    {
                        logger.Information("Viewer snapshot unlock canceled; previous copy unchanged");
                        return ViewerImportOutcome.Canceled;
                    }

                    key = await Task.Run(() => SnapshotKey.Derive(passphrase, info));
                }

                try
                {
                    var unlockedKey = key;
                    var (reader, manifest) = await Task.Run(() => SnapshotImporter.Import(archivePath, dbPath, unlockedKey));
                    await host.ImportedAsync(reader, manifest);
                    try
                    {
                        await Task.Run(() => host.RememberKey(unlockedKey));
                        logger.Information("Viewer snapshot key remembered");
                        return ViewerImportOutcome.Imported;
                    }
                    catch (Exception ex)
                    {
                        logger.Warning("Viewer snapshot loaded but key could not be remembered, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                        return ViewerImportOutcome.ImportedKeyNotRemembered;
                    }
                }
                catch (SnapshotAuthenticationException ex)
                {
                    logger.Information("Viewer snapshot unlock rejected; retrying passphrase entry");
                    key.Dispose();
                    key = null;
                    reason = PassphraseReason.WrongPassphrase;
                    unlockError = ex.Message;
                }
            }
        }
        finally
        {
            key?.Dispose();
        }
    }
}
