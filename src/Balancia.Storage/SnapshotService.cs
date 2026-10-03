using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed record SnapshotManifest(string Format, int SchemaVersion, string DatasetId, long Revision, DateTimeOffset ExportedAtUtc);

public sealed record SnapshotRestoreResult(string BackupPath, SnapshotManifest Manifest);

public sealed partial class LedgerStore
{
    public SnapshotRestoreResult RestoreSnapshot(string source, SnapshotKey? key = null) => LogOperation(() => RestoreSnapshotCore(source, key));

    private SnapshotRestoreResult RestoreSnapshotCore(string source, SnapshotKey? key)
    {
        using var archive = SnapshotEncryption.OpenArchive(Path.GetFullPath(source), key);
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
            Serilog.Log.ForContext<LedgerStore>().Information("Restore snapshot validated before replacement");
            using (var liveConnection = connections.Open())
            using (var backupConnection = new SqliteConnectionFactory(backup).Open())
            {
                liveConnection.BackupDatabase(backupConnection);
            }

            Serilog.Log.ForContext<LedgerStore>().Information("Restore recovery copy saved before replacement");

            SqliteConnection.ClearAllPools();
            File.Move(staged, path, true);
            Serilog.Log.ForContext<LedgerStore>().Information("Snapshot restored, Revision: '{Revision}', BackupPath: '{BackupPath}'", manifest.Revision, backup);
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

    public SnapshotManifest ValidateSnapshot(string source, SnapshotKey? key = null) => LogOperation(() => ValidateSnapshotCore(source, key));

    private SnapshotManifest ValidateSnapshotCore(string source, SnapshotKey? key)
    {
        var temporary = path + ".snapshot-check-" + Guid.NewGuid().ToString("N");
        try
        {
            return ExtractValidatedSnapshot(source, temporary, key);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static SnapshotManifest ExtractValidatedSnapshot(string source, string destination, SnapshotKey? key)
    {
        using var archive = SnapshotEncryption.OpenArchive(Path.GetFullPath(source), key);
        var manifest = ReadManifestEntry(archive);
        ExtractDatabaseEntry(archive, destination);
        ValidateExtractedDatabase(destination, manifest);
        return manifest;
    }

    private static SnapshotManifest ReadManifestEntry(ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Snapshot manifest is missing.");
        if (manifestEntry.Length > 64 * 1024)
        {
            throw new InvalidDataException("Snapshot manifest exceeds the supported size.");
        }
        SnapshotManifest? manifest;
        using (var input = manifestEntry.Open())
        using (var buffer = new MemoryStream())
        {
            CopyBounded(input, buffer, 64 * 1024);
            manifest = JsonSerializer.Deserialize<SnapshotManifest>(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
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
        if (database.Length > 512L * 1024 * 1024)
        {
            throw new InvalidDataException("Snapshot database exceeds the supported size.");
        }
        using var input = database.Open();
        using var output = File.Create(destination);
        CopyBounded(input, output, 512L * 1024 * 1024);
    }

    private static void CopyBounded(Stream input, Stream output, long maximum)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        try
        {
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                total += count;
                if (total > maximum)
                {
                    throw new InvalidDataException("Snapshot exceeds the supported size.");
                }

                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
        }
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

    public SnapshotManifest ExportSnapshot(string destination, SnapshotKey key) => LogOperation(() => ExportSnapshotCore(destination, key));

    private SnapshotManifest ExportSnapshotCore(string destination, SnapshotKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.CheckUsable();
        var full = Path.GetFullPath(destination);
        // Plaintext staging must stay local, never in the destination's synced folder.
        var temporary = Path.Combine(Path.GetTempPath(), "balancia-export-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            SnapshotManifest manifest;
            using (var source = connections.Open())
            using (var backup = new SqliteConnectionFactory(temporary).Open())
            {
                source.BackupDatabase(backup);
                using var command = backup.CreateCommand();
                command.CommandText = "SELECT dataset_id,revision FROM metadata WHERE id=1";
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Ledger metadata is missing.");
                }

                manifest = new SnapshotManifest("balancia-snapshot-1", 3, reader.GetString(0), reader.GetInt64(1), DateTimeOffset.UtcNow);
            }

            using var buffer = new MemoryStream();
            try
            {
                using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    archive.CreateEntryFromFile(temporary, "ledger.db", CompressionLevel.Optimal);
                    using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                    writer.Write(JsonSerializer.Serialize(manifest));
                }

                AtomicFileWriter.Write(full, stream => SnapshotEncryption.Write(stream,
                    buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), key));
                Serilog.Log.ForContext<LedgerStore>().Information("Encrypted snapshot saved atomically, Revision: '{Revision}'", manifest.Revision);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer.GetBuffer());
            }

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
