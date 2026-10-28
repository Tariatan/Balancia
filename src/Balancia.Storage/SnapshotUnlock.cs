using Serilog;

namespace Balancia.Storage;

/// <summary>Why a platform has to ask for the passphrase.</summary>
public enum PassphraseReason
{
    /// <summary>No remembered key matches this snapshot (first import, or a changed passphrase).</summary>
    Pairing,

    /// <summary>Loading the remembered key failed (reinstall, key invalidation, damaged cache).</summary>
    RememberedKeyUnavailable,

    /// <summary>The previous passphrase did not unlock the snapshot.</summary>
    WrongPassphrase,
}

/// <summary>What <see cref="SnapshotUnlock"/> needs from a platform: the remembered key and the passphrase prompt.</summary>
public interface ISnapshotUnlockHost
{
    /// <summary>The remembered key, or null when none was saved; throws when it cannot be read. The caller owns and disposes the key, so return a copy of a key that is still in use.</summary>
    SnapshotKey? LoadRememberedKey();

    /// <summary>Asks for the passphrase; null when the user cancels. <paramref name="error"/> is the previous failure on a retry. Runs on the caller's context.</summary>
    Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error);
}

/// <summary>The result of an unlocked operation, or that the user declined to enter the passphrase.</summary>
public sealed record UnlockResult<T>(bool Canceled, T? Value);

/// <summary>
/// Finds the key that opens a snapshot file: the remembered key when it matches the file, otherwise the passphrase the user types, asked
/// again after a wrong one. The operation runs with that key; a <see cref="SnapshotAuthenticationException"/> from it counts as a wrong
/// passphrase. The unlock disposes every key it holds. What happens to the key afterwards (remembering it, or not) belongs to the operation.
/// Heavy work runs off the caller's context; host callbacks run on it. Any other failure propagates.
/// </summary>
public static class SnapshotUnlock
{
    public static async Task<UnlockResult<T>> RunAsync<T>(string archivePath, ISnapshotUnlockHost host, Func<SnapshotKey, Task<T>> operation)
    {
        var logger = Log.ForContext(typeof(SnapshotUnlock));
        var info = await Task.Run(() => SnapshotEncryption.ReadInfo(archivePath));
        SnapshotKey? key = null;
        var reason = PassphraseReason.Pairing;
        string? unlockError = null;
        try
        {
            try
            {
                key = await Task.Run(host.LoadRememberedKey);
                logger.Information("Remembered snapshot key loaded, KeyAvailable: '{KeyAvailable}'", key is not null);
            }
            catch (Exception ex)
            {
                logger.Warning("Remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
                // Reinstallation, key invalidation, or a damaged cache requires pairing again.
                reason = PassphraseReason.RememberedKeyUnavailable;
            }

            if (key is not null && !key.Matches(info))
            {
                logger.Information("Remembered key does not match snapshot; pairing required");
                key.Dispose();
                key = null;
            }
            else if (key is not null)
            {
                logger.Information("Snapshot unlock using remembered key");
            }

            while (true)
            {
                if (key is null)
                {
                    logger.Information("Snapshot unlock prompting for passphrase");
                    var passphrase = await host.AskPassphraseAsync(reason, unlockError);
                    if (passphrase is null)
                    {
                        logger.Information("Snapshot unlock canceled");
                        return new UnlockResult<T>(true, default);
                    }

                    key = await Task.Run(() => SnapshotKey.Derive(passphrase, info));
                }

                try
                {
                    return new UnlockResult<T>(false, await operation(key));
                }
                catch (SnapshotAuthenticationException ex)
                {
                    logger.Information("Snapshot unlock rejected; retrying passphrase entry");
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
