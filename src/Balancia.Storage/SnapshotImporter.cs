using System.IO.Compression;

namespace Balancia.Storage;

/// <summary>Validates and applies a snapshot archive as a fresh local database, for read-only viewers (e.g. Android).</summary>
public static class SnapshotImporter
{
    public static (LedgerStore Store, SnapshotManifest Manifest) Import(string archivePath, string dbPath)
    {
        var manifest = new LedgerStore(dbPath).ValidateSnapshot(archivePath);
        var staged = dbPath + ".staged";
        using (var archive = ZipFile.OpenRead(archivePath))
        using (var input = archive.GetEntry("ledger.db")!.Open())
        using (var output = File.Create(staged))
        {
            input.CopyTo(output);
        }

        File.Move(staged, dbPath, true);
        return (new LedgerStore(dbPath), manifest);
    }
}
