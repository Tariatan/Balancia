using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed record SnapshotManifest(string Format, int SchemaVersion, string DatasetId, long Revision, DateTimeOffset ExportedAtUtc);

public sealed partial class LedgerStore
{
    public SnapshotManifest ExportSnapshot(string destination)
    {
        var full = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            SnapshotManifest manifest;
            using (var source = _connections.Open())
            using (var backup = new SqliteConnectionFactory(temporary).Open())
            {
                source.BackupDatabase(backup);
                using var command = source.CreateCommand();
                command.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
                using var reader = command.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Ledger metadata is missing.");
                manifest = new("balancia-snapshot-1", 3, reader.GetString(0), reader.GetInt64(1), DateTimeOffset.UtcNow);
            }
            var staged = full + ".staged-" + Guid.NewGuid().ToString("N");
            using (var archive = ZipFile.Open(staged, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(temporary, "ledger.db", CompressionLevel.Optimal);
                using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest));
            }
            File.Move(staged, full, true);
            return manifest;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
