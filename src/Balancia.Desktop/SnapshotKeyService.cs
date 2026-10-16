using System.ComponentModel;
using System.Security.Cryptography;
using Balancia.Storage;
using Serilog;

namespace Balancia.Desktop;

internal enum KeyLoadResult
{
    Loaded,
    Missing,
    Unavailable,
}

/// <summary>Owns the snapshot key for the running application: loads the remembered one, replaces it and disposes it.</summary>
internal sealed class SnapshotKeyService(ISnapshotKeyStore store, Func<string> pathProvider) : IDisposable
{
    public SnapshotKey? Key { get; private set; }

    public bool IsConfigured => Key is not null;

    /// <summary>Loads the remembered key unless one is already in use. A protected-storage failure reports <see cref="KeyLoadResult.Unavailable"/>.</summary>
    public KeyLoadResult Load()
    {
        if (Key is not null)
        {
            return KeyLoadResult.Loaded;
        }

        Log.Information("Loading remembered Windows snapshot key");
        try
        {
            Key = store.Load(pathProvider());
            Log.Information("Windows snapshot key load completed, KeyAvailable: '{KeyAvailable}'", Key is not null);
            return Key is null ? KeyLoadResult.Missing : KeyLoadResult.Loaded;
        }
        catch (Exception ex) when (ex is Win32Exception or CryptographicException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warning("Remembered snapshot key unavailable, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            return KeyLoadResult.Unavailable;
        }
    }

    /// <summary>Uses a key without touching protected storage.</summary>
    public void Use(SnapshotKey key)
    {
        Key?.Dispose();
        Key = key;
    }

    /// <summary>Remembers the key, then switches to it. A failed save leaves the previous key in place and the caller owns <paramref name="key"/>.</summary>
    public void Replace(SnapshotKey key)
    {
        store.Save(pathProvider(), key);
        Use(key);
    }

    public void Dispose()
    {
        Key?.Dispose();
        Key = null;
    }
}
