using Balancia.Storage;

namespace Balancia.Desktop.Tests;

/// <summary>An in-memory <see cref="ISnapshotKeyStore"/> whose failures tests can script, so no DPAPI is needed.</summary>
internal sealed class FakeKeyStore : ISnapshotKeyStore
{
    public Exception? LoadFailure { get; set; }

    public Exception? SaveFailure { get; set; }

    /// <summary>Returned by <see cref="Load"/> when set.</summary>
    public SnapshotKey? Stored { get; set; }

    public int SaveCount { get; private set; }

    public SnapshotKey? Load(string path) => LoadFailure is null ? Stored : throw LoadFailure;

    public void Save(string path, SnapshotKey key)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        SaveCount++;
        Stored = SnapshotKey.ImportSecret(key.ExportSecret());
    }
}
