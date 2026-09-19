using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed record SnapshotManifest(string Format, int SchemaVersion, string DatasetId, long Revision, DateTimeOffset ExportedAtUtc);

public sealed partial class LedgerStore
{
    public string RestoreSnapshot(string source)
    {
        var manifest = ValidateSnapshot(source);
        var current = ReadSnapshot();
        if (manifest.DatasetId != ReadDatasetId()) throw new InvalidDataException("Snapshot belongs to a different ledger dataset.");
        if (manifest.Revision < current.Revision) throw new InvalidDataException("An older snapshot cannot replace the current ledger.");
        var staged = _path + ".restore-" + Guid.NewGuid().ToString("N");
        var backup = _path + ".pre-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            using (var archive = ZipFile.OpenRead(Path.GetFullPath(source)))
            using (var input = archive.GetEntry("ledger.db")!.Open())
            using (var output = File.Create(staged)) input.CopyTo(output);
            SqliteConnection.ClearAllPools();
            File.Copy(_path, backup, true);
            File.Move(staged, _path, true);
            return backup;
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private string ReadDatasetId()
    {
        using var c = _connections.Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT dataset_id FROM metadata WHERE id=1";
        return Convert.ToString(cmd.ExecuteScalar())!;
    }

    public SnapshotManifest ValidateSnapshot(string source)
    {
        using var archive = ZipFile.OpenRead(Path.GetFullPath(source));
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Snapshot manifest is missing.");
        SnapshotManifest? manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<SnapshotManifest>(reader.ReadToEnd());
        if (manifest is null || manifest.Format != "balancia-snapshot-1" || manifest.SchemaVersion != 3)
            throw new InvalidDataException("Snapshot format or schema version is unsupported.");
        var database = archive.GetEntry("ledger.db") ?? throw new InvalidDataException("Snapshot database is missing.");
        var temporary = _path + ".snapshot-check-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = database.Open()) using (var output = File.Create(temporary)) input.CopyTo(output);
            using var connection = new SqliteConnectionFactory(temporary).Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Snapshot database integrity check failed.");
            command.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt64(command.ExecuteScalar());
            command.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
            using var row = command.ExecuteReader();
            if (!row.Read() || version != manifest.SchemaVersion || row.GetString(0) != manifest.DatasetId || row.GetInt64(1) != manifest.Revision)
                throw new InvalidDataException("Snapshot manifest does not match its database.");
            return manifest;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

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
