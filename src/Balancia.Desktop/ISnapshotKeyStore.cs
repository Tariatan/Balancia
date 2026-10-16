using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>Persists the derived snapshot key through platform-protected storage.</summary>
internal interface ISnapshotKeyStore
{
    /// <summary>The remembered key, or null when none was saved.</summary>
    SnapshotKey? Load(string path);

    void Save(string path, SnapshotKey key);
}
