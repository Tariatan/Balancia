using System.IO.Compression;
using System.Text.Json;
using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "balancia-snapshot-" + Guid.NewGuid());
    private readonly LedgerStore _store;
    public SnapshotTests() { Directory.CreateDirectory(_dir); _store = new(Path.Combine(_dir, "ledger.db")); _store.Initialize(); _store.SaveAccount(null, "Cash", new(2026, 1, 1), new(100)); }

    [Fact]
    public void ExportContainsConsistentDatabaseAndManifest()
    {
        var path = Path.Combine(_dir, "snapshot.balancia");
        var manifest = _store.ExportSnapshot(path);
        Assert.True(File.Exists(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Contains(archive.Entries, e => e.FullName == "ledger.db");
        var json = new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd();
        var read = JsonSerializer.Deserialize<SnapshotManifest>(json)!;
        Assert.Equal(manifest.DatasetId, read.DatasetId);
        Assert.Equal(3, read.SchemaVersion);
        using (var entry = archive.GetEntry("ledger.db")!.Open())
        using (var file = File.Create(Path.Combine(_dir, "extracted.db"))) entry.CopyTo(file);
        using var check = new SqliteConnectionFactory(Path.Combine(_dir, "extracted.db")).Open();
        using var command = check.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM accounts";
        Assert.Equal(1L, command.ExecuteScalar());
        Assert.Equal(manifest, _store.ValidateSnapshot(path));
    }

    [Fact]
    public void CorruptSnapshotIsRejected()
    {
        var path = Path.Combine(_dir, "bad.balancia");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => _store.ValidateSnapshot(path));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
