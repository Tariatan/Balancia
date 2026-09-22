using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed record SnapshotManifest(string Format, int SchemaVersion, string DatasetId, long Revision, DateTimeOffset ExportedAtUtc);

public sealed record SnapshotRestoreResult(string BackupPath, SnapshotManifest Manifest);

public sealed partial class LedgerStore
{
    public SnapshotRestoreResult RestoreSnapshot(string source)
    {
        using var archive = ZipFile.OpenRead(Path.GetFullPath(source));
        var manifest = ReadManifestEntry(archive);
        var (datasetId, revision) = ReadDatasetIdAndRevision();
        if (manifest.DatasetId != datasetId)
        {
            throw new InvalidDataException("Snapshot belongs to a different ledger dataset.");
        }

        if (manifest.Revision < revision)
        {
            throw new InvalidDataException("An older snapshot cannot replace the current ledger.");
        }

        var staged = path + ".restore-" + Guid.NewGuid().ToString("N");
        var backup = path + ".pre-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            ExtractDatabaseEntry(archive, staged);
            ValidateExtractedDatabase(staged, manifest);
            using (var liveConnection = connections.Open())
            using (var backupConnection = new SqliteConnectionFactory(backup).Open())
            {
                liveConnection.BackupDatabase(backupConnection);
            }

            SqliteConnection.ClearAllPools();
            File.Move(staged, path, true);
            return new SnapshotRestoreResult(backup, manifest);
        }
        finally
        {
            if (File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }

    private (string DatasetId, long Revision) ReadDatasetIdAndRevision()
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
        using var reader = cmd.ExecuteReader();
        reader.Read();
        var result = (reader.GetString(0), reader.GetInt64(1));
        reader.Close();
        tx.Commit();
        return result;
    }

    public SnapshotManifest ValidateSnapshot(string source)
    {
        using var archive = ZipFile.OpenRead(Path.GetFullPath(source));
        var manifest = ReadManifestEntry(archive);
        var temporary = path + ".snapshot-check-" + Guid.NewGuid().ToString("N");
        try
        {
            ExtractDatabaseEntry(archive, temporary);
            ValidateExtractedDatabase(temporary, manifest);
            return manifest;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static SnapshotManifest ReadManifestEntry(ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Snapshot manifest is missing.");
        SnapshotManifest? manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            manifest = JsonSerializer.Deserialize<SnapshotManifest>(reader.ReadToEnd());
        }

        if (manifest is null || manifest.Format != "balancia-snapshot-1" || manifest.SchemaVersion != 3)
        {
            throw new InvalidDataException("Snapshot format or schema version is unsupported.");
        }

        return manifest;
    }

    private static void ExtractDatabaseEntry(ZipArchive archive, string destination)
    {
        var database = archive.GetEntry("ledger.db") ?? throw new InvalidDataException("Snapshot database is missing.");
        using var input = database.Open();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private static void ValidateExtractedDatabase(string databasePath, SnapshotManifest manifest)
    {
        using var connection = new SqliteConnectionFactory(databasePath).Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Snapshot database integrity check failed.");
        }

        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
        using var row = command.ExecuteReader();
        if (!row.Read() || version != manifest.SchemaVersion || row.GetString(0) != manifest.DatasetId || row.GetInt64(1) != manifest.Revision)
        {
            throw new InvalidDataException("Snapshot manifest does not match its database.");
        }
    }

    public SnapshotManifest ExportSnapshot(string destination)
    {
        var full = Path.GetFullPath(destination);
        var temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            SnapshotManifest manifest;
            using (var source = connections.Open())
            using (var backup = new SqliteConnectionFactory(temporary).Open())
            {
                source.BackupDatabase(backup);
                using var command = source.CreateCommand();
                command.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Ledger metadata is missing.");
                }

                manifest = new SnapshotManifest("balancia-snapshot-1", 3, reader.GetString(0), reader.GetInt64(1), DateTimeOffset.UtcNow);
            }

            AtomicFileWriter.Write(full, stream =>
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                archive.CreateEntryFromFile(temporary, "ledger.db", CompressionLevel.Optimal);
                using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest));
            });
            return manifest;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
