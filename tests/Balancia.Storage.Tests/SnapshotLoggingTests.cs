using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Balancia.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Balancia.Storage.Tests;

[Collection("Logging")]
public sealed class SnapshotLoggingTests : IDisposable
{
    private const string Passphrase = "synthetic-private-passphrase-logging";
    private const string Description = "synthetic-private-description-logging";
    private const string Notes = "synthetic-private-notes-logging";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-log-test-" + Guid.NewGuid().ToString("N"));
    private readonly CapturingSink sink = new();
    private readonly ILogger previousLogger = Log.Logger;
    private readonly Logger logger;
    private readonly SnapshotKey key = SnapshotKey.Create(Passphrase);
    private readonly LedgerStore store;
    private string ArchivePath => Path.Combine(directory, "Snapshot.balancia");
    private string ViewerPath => Path.Combine(directory, "viewer.db");

    public SnapshotLoggingTests()
    {
        logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        Directory.CreateDirectory(directory);
        store = new LedgerStore(Path.Combine(directory, "ledger.db"));
        store.Initialize();
        var account = store.SaveAccount(null, "Synthetic private account", new DateOnly(2026, 1, 1), Money.Zero);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2),
            Description, new Money(98765432), account) with { Memo = Notes });
    }

    [Fact]
    public void SnapshotOperations_WithPrivateData_LogCorrelatedMilestonesWithoutSensitiveContents()
    {
        // Arrange: the fixture creates a synthetic ledger and a captured logger.
        // Act
        store.ExportSnapshot(ArchivePath, key);
        SnapshotImporter.Import(ArchivePath, ViewerPath, key);
        store.RestoreSnapshot(ArchivePath, key);

        // Assert
        var events = sink.Events.ToArray();
        var importEvents = events.Where(e => Context(e) == typeof(SnapshotImporter).FullName).ToArray();
        Assert.Equal(3, importEvents.Length); // Start, validated before replacement, completion.
        Assert.Single(importEvents.Select(e => e.Properties["OperationId"].ToString()).Distinct());
        Assert.Contains(events, e => e.RenderMessage().Contains("authenticated and decrypted") && e.Properties.ContainsKey("OperationId"));
        Assert.True(Array.FindIndex(events, e => e.RenderMessage().Contains("recovery copy saved")) <
            Array.FindIndex(events, e => e.RenderMessage().StartsWith("Snapshot restored")));
        Assert.DoesNotContain(events, e => e.Level >= LogEventLevel.Warning);

        var output = string.Join("\n", events.Select(e => e.RenderMessage() + string.Join(" ", e.Properties.Values) + e.Exception));
        foreach (var secret in new[] { Passphrase, Description, Notes, "987654.32", "98765432", key.Info.Salt })
        {
            Assert.DoesNotContain(secret, output);
        }
        var exportedKey = key.ExportSecret();
        try
        {
            Assert.DoesNotContain(Convert.ToBase64String(exportedKey), output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportedKey);
        }
    }

    [Fact]
    public void Import_WithWrongOrMissingKey_LogsRejectionAndPreservesPreviousViewer()
    {
        // Arrange
        store.ExportSnapshot(ArchivePath, key);
        SnapshotImporter.Import(ArchivePath, ViewerPath, key);
        var previous = File.ReadAllBytes(ViewerPath);
        sink.Events.Clear();
        using var wrongKey = SnapshotKey.Derive("synthetic wrong passphrase", key.Info);

        // Act
        Assert.Throws<InvalidDataException>(() => SnapshotImporter.Import(ArchivePath, ViewerPath, wrongKey));
        Assert.Throws<InvalidOperationException>(() => store.ValidateSnapshot(ArchivePath));

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(ViewerPath));
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Warning);
        var rejected = Assert.Single(sink.Events, e => e.RenderMessage().StartsWith("Viewer snapshot import rejected"));
        Assert.Equal(new ScalarValue(false), rejected.Properties["DatabaseReplaced"]);
        Assert.Contains(sink.Events, e => e.RenderMessage().StartsWith("Snapshot authentication rejected"));
        Assert.Contains(sink.Events, e => e.RenderMessage().StartsWith("Storage operation rejected"));
    }

    [Fact]
    public void Import_WithMalformedManifest_LogsExpectedRejectionWithoutParserInput()
    {
        // Arrange
        using (var archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write("{\"SchemaVersion\":\"" + Description + "\"}");
        }
        sink.Events.Clear();

        // Act
        Assert.Throws<System.Text.Json.JsonException>(() => SnapshotImporter.Import(ArchivePath, ViewerPath));

        // Assert
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Warning || e.Exception is not null);
        Assert.DoesNotContain(Description, string.Join("\n", sink.Events.Select(e => e.RenderMessage())));
        Assert.Contains(sink.Events, e => e.RenderMessage().StartsWith("Viewer snapshot import rejected"));
    }

    [Fact]
    public void Import_WithMissingFile_LogsErrorAndPreservesPreviousViewer()
    {
        // Arrange
        store.ExportSnapshot(ArchivePath, key);
        SnapshotImporter.Import(ArchivePath, ViewerPath, key);
        var previous = File.ReadAllBytes(ViewerPath);
        File.Delete(ArchivePath);
        sink.Events.Clear();

        // Act
        Assert.Throws<FileNotFoundException>(() => SnapshotImporter.Import(ArchivePath, ViewerPath, key));

        // Assert
        Assert.Equal(previous, File.ReadAllBytes(ViewerPath));
        var failed = Assert.Single(sink.Events, e => e.Level == LogEventLevel.Error);
        Assert.Equal(new ScalarValue(false), failed.Properties["DatabaseReplaced"]);
        Assert.Equal(new ScalarValue(nameof(FileNotFoundException)), failed.Properties["FailureType"]);
        Assert.True(failed.Properties.ContainsKey("HResult"));
        Assert.Null(failed.Exception);
    }

    [Fact]
    public void Import_WithDisposedKey_LogsUnexpectedFailure()
    {
        // Arrange
        store.ExportSnapshot(ArchivePath, key);
        key.Dispose();
        sink.Events.Clear();

        // Act
        Assert.Throws<ObjectDisposedException>(() => SnapshotImporter.Import(ArchivePath, ViewerPath, key));

        // Assert
        var failed = Assert.Single(sink.Events, e => e.Level == LogEventLevel.Error);
        Assert.Equal(new ScalarValue(nameof(ObjectDisposedException)), failed.Properties["FailureType"]);
        Assert.Equal(new ScalarValue(false), failed.Properties["DatabaseReplaced"]);
    }

    private static string? Context(LogEvent entry) => (entry.Properties.GetValueOrDefault("SourceContext") as ScalarValue)?.Value as string;

    public void Dispose()
    {
        Log.Logger = previousLogger;
        logger.Dispose();
        key.Dispose();
        Directory.Delete(directory, true);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        internal ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
