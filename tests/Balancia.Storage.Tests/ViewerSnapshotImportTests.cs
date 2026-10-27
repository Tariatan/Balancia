using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class ViewerSnapshotImportTests : IDisposable
{
    private const string Passphrase = "synthetic viewer passphrase";

    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-viewer-import-" + Guid.NewGuid());
    private readonly SnapshotKey key = SnapshotKey.Create(Passphrase);
    private readonly LedgerStore source;
    private readonly string archive;
    private readonly string viewerPath;

    public ViewerSnapshotImportTests()
    {
        Directory.CreateDirectory(dir);
        viewerPath = Path.Combine(dir, "viewer.db");
        archive = Path.Combine(dir, "incoming.balancia");
        source = new LedgerStore(Path.Combine(dir, "source.db"));
        source.Initialize();
        source.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(500));
        source.ExportSnapshot(archive, key);
    }

    private sealed class FakeHost : IViewerSnapshotHost
    {
        public SnapshotKey? Remembered { get; init; }
        public Exception? LoadFailure { get; init; }
        public Exception? RememberFailure { get; init; }
        public List<string?> Answers { get; } = [];
        public List<(PassphraseReason Reason, string? Error)> Prompts { get; } = [];
        public List<string> Events { get; } = [];
        public byte[]? RememberedSecret { get; private set; }
        public SnapshotManifest? Imported { get; private set; }
        public List<SynchronizationContext?> Contexts { get; } = [];

        public SnapshotKey? LoadRememberedKey()
        {
            Events.Add("load");
            return LoadFailure is null ? Remembered : throw LoadFailure;
        }

        public void RememberKey(SnapshotKey key)
        {
            Events.Add("remember");
            RememberedSecret = key.ExportSecret();
            if (RememberFailure is not null)
            {
                throw RememberFailure;
            }
        }

        public Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error)
        {
            Events.Add("ask");
            Contexts.Add(SynchronizationContext.Current);
            Prompts.Add((reason, error));
            return Task.FromResult(Answers[Prompts.Count - 1]);
        }

        public Task ImportedAsync(LedgerReader reader, SnapshotManifest manifest)
        {
            Events.Add("imported");
            Contexts.Add(SynchronizationContext.Current);
            Imported = manifest;
            return Task.CompletedTask;
        }
    }

    private byte[] ExpectedSecret() => SnapshotKey.Derive(Passphrase, key.Info).ExportSecret();

    [Fact]
    public async Task RunAsync_FirstImportWithTheCorrectPassphrase_PromptsOnceImportsAndRemembersTheKey()
    {
        // Arrange
        var host = new FakeHost { Answers = { Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Equal([(PassphraseReason.Pairing, (string?)null)], host.Prompts);
        Assert.Equal(["load", "ask", "imported", "remember"], host.Events);
        Assert.Equal(ExpectedSecret(), host.RememberedSecret);
        Assert.Equal(500, LedgerReader.Open(viewerPath).ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public async Task RunAsync_RememberedKeyMatches_ImportsWithoutPromptingAndReleasesTheKey()
    {
        // Arrange
        var remembered = SnapshotKey.Derive(Passphrase, key.Info);
        var host = new FakeHost { Remembered = remembered };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Empty(host.Prompts);
        Assert.Equal(["load", "imported", "remember"], host.Events);
        Assert.Throws<ObjectDisposedException>(() => remembered.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_RememberedKeyForAnotherPassphrase_ReleasesItAndAsksForPairing()
    {
        // Arrange
        var stale = SnapshotKey.Create("synthetic previous passphrase");
        var host = new FakeHost { Remembered = stale, Answers = { Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Equal(PassphraseReason.Pairing, Assert.Single(host.Prompts).Reason);
        Assert.Throws<ObjectDisposedException>(() => stale.ExportSecret());
        Assert.Equal(ExpectedSecret(), host.RememberedSecret);
    }

    [Fact]
    public async Task RunAsync_RememberedKeyCannotBeRead_AsksWithThatReason()
    {
        // Arrange
        var host = new FakeHost { LoadFailure = new InvalidDataException("damaged cache"), Answers = { Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Equal((PassphraseReason.RememberedKeyUnavailable, (string?)null), Assert.Single(host.Prompts));
    }

    [Fact]
    public async Task RunAsync_UserCancels_KeepsThePreviousCopyAndRemembersNothing()
    {
        // Arrange
        SnapshotImporter.Import(archive, viewerPath, key);
        var previous = File.ReadAllBytes(viewerPath);
        var host = new FakeHost { Answers = { null } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Canceled, outcome);
        Assert.Null(host.Imported);
        Assert.Null(host.RememberedSecret);
        Assert.Equal(previous, File.ReadAllBytes(viewerPath));
    }

    [Fact]
    public async Task RunAsync_WrongPassphraseThenCorrect_AsksAgainWithTheFailureMessage()
    {
        // Arrange
        var host = new FakeHost { Answers = { "synthetic wrong passphrase", Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Equal(2, host.Prompts.Count);
        Assert.Equal(PassphraseReason.Pairing, host.Prompts[0].Reason);
        Assert.Equal(PassphraseReason.WrongPassphrase, host.Prompts[1].Reason);
        Assert.Equal(new SnapshotAuthenticationException().Message, host.Prompts[1].Error);
        Assert.Equal(ExpectedSecret(), host.RememberedSecret);
    }

    [Fact]
    public async Task RunAsync_RememberedKeyDoesNotUnlockTheFile_AsksAgainInsteadOfFailing()
    {
        // Arrange
        var sameParametersWrongSecret = SnapshotKey.Derive("synthetic wrong passphrase", key.Info);
        var host = new FakeHost { Remembered = sameParametersWrongSecret, Answers = { Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Imported, outcome);
        Assert.Equal(PassphraseReason.WrongPassphrase, Assert.Single(host.Prompts).Reason);
        Assert.Throws<ObjectDisposedException>(() => sameParametersWrongSecret.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_WrongPassphraseThenCancel_KeepsThePreviousCopy()
    {
        // Arrange
        SnapshotImporter.Import(archive, viewerPath, key);
        var previous = File.ReadAllBytes(viewerPath);
        var host = new FakeHost { Answers = { "synthetic wrong passphrase", null } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.Canceled, outcome);
        Assert.Equal(previous, File.ReadAllBytes(viewerPath));
        Assert.Null(host.RememberedSecret);
    }

    [Fact]
    public async Task RunAsync_KeyCannotBeRemembered_StillImportsAndSaysSo()
    {
        // Arrange
        var host = new FakeHost { RememberFailure = new IOException("keystore"), Answers = { Passphrase } };

        // Act
        var outcome = await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        Assert.Equal(ViewerImportOutcome.ImportedKeyNotRemembered, outcome);
        Assert.NotNull(host.Imported);
        Assert.Equal(500, LedgerReader.Open(viewerPath).ReadSummary().Accounts.Single().Balance.Centimes);
    }

    [Fact]
    public async Task RunAsync_DamagedArchive_ThrowsBeforeAskingAnything()
    {
        // Arrange
        File.WriteAllText(archive, "not a snapshot");
        var host = new FakeHost();

        // Act
        Task Act() => ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

        // Assert
        await Assert.ThrowsAsync<InvalidDataException>(Act);
        Assert.Empty(host.Events);
        Assert.False(File.Exists(viewerPath));
    }

    [Fact]
    public async Task RunAsync_OlderSnapshotThanTheViewerCopy_ThrowsKeepsTheCopyAndRemembersNothing()
    {
        // Arrange
        var older = Path.Combine(dir, "older.balancia");
        source.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 2), "Gift",
            new Money(50), source.ReadSnapshot().Accounts.Single().Id));
        var newer = Path.Combine(dir, "newer.balancia");
        source.ExportSnapshot(newer, key);
        File.Copy(archive, older);
        SnapshotImporter.Import(newer, viewerPath, key);
        var previous = File.ReadAllBytes(viewerPath);
        var host = new FakeHost { Answers = { Passphrase } };

        // Act
        Task Act() => ViewerSnapshotImport.RunAsync(older, viewerPath, host);

        // Assert
        var error = await Assert.ThrowsAsync<InvalidDataException>(Act);
        Assert.Equal("An older snapshot cannot replace the current viewer copy.", error.Message);
        Assert.Equal(previous, File.ReadAllBytes(viewerPath));
        Assert.Null(host.RememberedSecret);
        Assert.DoesNotContain("imported", host.Events);
    }

    [Fact]
    public async Task RunAsync_HostCallbacks_RunOnTheCallersSynchronizationContext()
    {
        // Arrange
        var context = new PostingContext();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var host = new FakeHost { Answers = { "synthetic wrong passphrase", Passphrase } };

            // Act
            await ViewerSnapshotImport.RunAsync(archive, viewerPath, host);

            // Assert
            Assert.Equal(3, host.Contexts.Count);
            Assert.All(host.Contexts, seen => Assert.Same(context, seen));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <summary>Resumes continuations on a pool thread that reports this context as current, like a UI thread would.</summary>
    private sealed class PostingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => ThreadPool.QueueUserWorkItem(_ =>
        {
            SetSynchronizationContext(this);
            d(state);
        });
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(dir, true);
    }
}
