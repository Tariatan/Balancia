namespace Balancia.Storage;

/// <summary>The rule for whether a snapshot may replace what a ledger or viewer already holds; Windows restore and the viewer import both apply it.</summary>
internal static class SnapshotAcceptance
{
    /// <summary>Throws <see cref="InvalidDataException"/> for a snapshot of another dataset or an earlier revision; the same revision is accepted.</summary>
    /// <param name="target">What the snapshot would replace, named in the message ("ledger" or "viewer copy").</param>
    internal static void Require(SnapshotManifest manifest, (string DatasetId, long Revision) current, string target)
    {
        if (manifest.DatasetId != current.DatasetId)
        {
            throw new InvalidDataException("Snapshot belongs to a different ledger dataset.");
        }

        if (manifest.Revision < current.Revision)
        {
            throw new InvalidDataException($"An older snapshot cannot replace the current {target}.");
        }
    }
}
