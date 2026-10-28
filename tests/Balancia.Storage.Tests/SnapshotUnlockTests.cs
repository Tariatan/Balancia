using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotUnlockTests : IDisposable
{
    private const string Passphrase = "synthetic unlock passphrase";

    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-unlock-" + Guid.NewGuid());
    private readonly SnapshotKey key = SnapshotKey.Create(Passphrase);
    private readonly LedgerStore source;
    private readonly string archive;

    public SnapshotUnlockTests()
    {
        Directory.CreateDirectory(dir);
        archive = Path.Combine(dir, "incoming.balancia");
        source = new LedgerStore(Path.Combine(dir, "source.db"));
        source.Initialize();
        source.SaveAccount(null, "Cash", new DateOnly(2026, 1, 1), new Money(500));
        source.ExportSnapshot(archive, key);
    }

    private sealed class FakeHost : ISnapshotUnlockHost
    {
        public SnapshotKey? Remembered { get; init; }
        public Exception? LoadFailure { get; init; }
        public List<string?> Answers { get; } = [];
        public List<(PassphraseReason Reason, string? Error)> Prompts { get; } = [];

        public SnapshotKey? LoadRememberedKey() => LoadFailure is null ? Remembered : throw LoadFailure;

        public Task<string?> AskPassphraseAsync(PassphraseReason reason, string? error)
        {
            Prompts.Add((reason, error));
            return Task.FromResult(Answers[Prompts.Count - 1]);
        }
    }

    /// <summary>The operation under test: it only opens the archive, which fails with <see cref="SnapshotAuthenticationException"/> for a wrong key.</summary>
    private Func<SnapshotKey, Task<SnapshotManifest>> Validate(List<SnapshotKey>? used = null) => unlocked =>
    {
        used?.Add(unlocked);
        return Task.Run(() => source.ValidateSnapshot(archive, unlocked));
    };

    [Fact]
    public async Task RunAsync_RememberedKeyMatches_RunsWithoutPromptingAndReleasesTheKey()
    {
        // Arrange
        var remembered = SnapshotKey.Derive(Passphrase, key.Info);
        var host = new FakeHost { Remembered = remembered };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.NotNull(result.Value);
        Assert.Empty(host.Prompts);
        Assert.Throws<ObjectDisposedException>(() => remembered.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_RememberedKeyForAnotherPassphrase_ReleasesItAndAsksForPairing()
    {
        // Arrange
        using var other = SnapshotKey.Create("synthetic other passphrase");
        var copy = SnapshotKey.ImportSecret(other.ExportSecret());
        var host = new FakeHost { Remembered = copy, Answers = { Passphrase } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.Equal([(PassphraseReason.Pairing, (string?)null)], host.Prompts);
        Assert.Throws<ObjectDisposedException>(() => copy.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_RememberedKeyCannotBeRead_AsksWithThatReason()
    {
        // Arrange
        var host = new FakeHost { LoadFailure = new InvalidDataException("damaged"), Answers = { Passphrase } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.Equal([(PassphraseReason.RememberedKeyUnavailable, (string?)null)], host.Prompts);
    }

    [Fact]
    public async Task RunAsync_UserCancels_ReportsItAndNeverRunsTheOperation()
    {
        // Arrange
        var host = new FakeHost { Answers = { null } };
        var used = new List<SnapshotKey>();

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate(used));

        // Assert
        Assert.True(result.Canceled);
        Assert.Null(result.Value);
        Assert.Empty(used);
    }

    [Fact]
    public async Task RunAsync_WrongPassphraseThenCorrect_AsksAgainWithTheFailureMessage()
    {
        // Arrange
        var host = new FakeHost { Answers = { "synthetic wrong passphrase", Passphrase } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.Equal(2, host.Prompts.Count);
        Assert.Equal((PassphraseReason.Pairing, (string?)null), host.Prompts[0]);
        Assert.Equal((PassphraseReason.WrongPassphrase, "The snapshot passphrase is incorrect or the file is damaged."), host.Prompts[1]);
    }

    [Fact]
    public async Task RunAsync_SeveralWrongPassphrases_KeepsAskingUntilCorrectOrCanceled()
    {
        // Arrange
        var host = new FakeHost { Answers = { "wrong one", "wrong two", "wrong three", Passphrase } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.Equal(4, host.Prompts.Count);
        Assert.All(host.Prompts.Skip(1), prompt => Assert.Equal(PassphraseReason.WrongPassphrase, prompt.Reason));
    }

    [Fact]
    public async Task RunAsync_WrongPassphraseThenCancel_ReportsCancel()
    {
        // Arrange
        var host = new FakeHost { Answers = { "synthetic wrong passphrase", null } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.True(result.Canceled);
        Assert.Equal(2, host.Prompts.Count);
    }

    [Fact]
    public async Task RunAsync_RememberedKeyDoesNotUnlockTheFile_AsksAgainInsteadOfFailing()
    {
        // Arrange
        // Same parameters as the file but another secret, so the key matches the header and still fails to authenticate.
        var sameParameters = SnapshotKey.Derive("synthetic other passphrase", key.Info);
        var host = new FakeHost { Remembered = sameParameters, Answers = { Passphrase } };

        // Act
        var result = await SnapshotUnlock.RunAsync(archive, host, Validate());

        // Assert
        Assert.False(result.Canceled);
        Assert.Equal([(PassphraseReason.WrongPassphrase, (string?)"The snapshot passphrase is incorrect or the file is damaged.")], host.Prompts);
        Assert.Throws<ObjectDisposedException>(() => sameParameters.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_OperationFailsForAnotherReason_PropagatesReleasesTheKeyAndDoesNotAsk()
    {
        // Arrange
        var remembered = SnapshotKey.Derive(Passphrase, key.Info);
        var host = new FakeHost { Remembered = remembered };

        // Act
        Task Act() => SnapshotUnlock.RunAsync<int>(archive, host, _ => throw new IOException("disk full"));

        // Assert
        await Assert.ThrowsAsync<IOException>(Act);
        Assert.Empty(host.Prompts);
        Assert.Throws<ObjectDisposedException>(() => remembered.ExportSecret());
    }

    [Fact]
    public async Task RunAsync_DamagedArchive_ThrowsBeforeAskingAnything()
    {
        // Arrange
        var damaged = Path.Combine(dir, "damaged.balancia");
        File.WriteAllBytes(damaged, [1, 2, 3]);
        var host = new FakeHost { Answers = { Passphrase } };

        // Act
        Task Act() => SnapshotUnlock.RunAsync(damaged, host, Validate());

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(Act);
        Assert.Empty(host.Prompts);
    }

    [Fact]
    public async Task RunAsync_OperationKeyIsReleasedAfterTheOperation()
    {
        // Arrange
        var host = new FakeHost { Answers = { Passphrase } };
        var used = new List<SnapshotKey>();

        // Act
        await SnapshotUnlock.RunAsync(archive, host, Validate(used));

        // Assert
        var typed = Assert.Single(used);
        Assert.Throws<ObjectDisposedException>(() => typed.ExportSecret());
    }

    public void Dispose()
    {
        key.Dispose();
        Directory.Delete(dir, true);
    }
}
