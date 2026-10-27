using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SnapshotAcceptanceTests
{
    private static SnapshotManifest Manifest(string dataset, long revision) =>
        new("balancia-snapshot-1", 1, dataset, revision, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(5, 5)] // the same revision again
    [InlineData(6, 5)] // a newer revision
    public void Require_SameDatasetNotOlder_Accepts(long snapshotRevision, long currentRevision)
    {
        // Act
        var error = Record.Exception(() =>
            SnapshotAcceptance.Require(Manifest("dataset-a", snapshotRevision), ("dataset-a", currentRevision), "ledger"));

        // Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("viewer copy")]
    public void Require_OlderRevision_RejectsNamingWhatItWouldReplace(string target)
    {
        // Act
        void Act() => SnapshotAcceptance.Require(Manifest("dataset-a", 4), ("dataset-a", 5), target);

        // Assert
        var error = Assert.Throws<InvalidDataException>(Act);
        Assert.Equal($"An older snapshot cannot replace the current {target}.", error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)] // a higher revision does not make another dataset acceptable
    public void Require_DifferentDataset_Rejects(long snapshotRevision)
    {
        // Act
        void Act() => SnapshotAcceptance.Require(Manifest("dataset-b", snapshotRevision), ("dataset-a", 5), "ledger");

        // Assert
        var error = Assert.Throws<InvalidDataException>(Act);
        Assert.Equal("Snapshot belongs to a different ledger dataset.", error.Message);
    }
}
