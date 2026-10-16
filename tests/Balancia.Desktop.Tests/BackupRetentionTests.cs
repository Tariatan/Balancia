using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class BackupRetentionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-retention-" + Guid.NewGuid().ToString("N"));

    public BackupRetentionTests() => Directory.CreateDirectory(directory);

    private string Create(string name, DateTime utc)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, utc);
        return path;
    }

    [Fact]
    public void Apply_MoreThanThreeOnOneDay_KeepsTheNewestThree()
    {
        // Arrange
        var files = Enumerable.Range(0, 5)
            .Select(hour => Create($"backup-{hour}.balancia", new DateTime(2026, 5, 1, hour, 0, 0, DateTimeKind.Utc)))
            .ToArray();

        // Act
        var (removed, retained) = BackupRetention.Apply(directory, TimeZoneInfo.Utc);

        // Assert
        Assert.Equal((2, 3), (removed, retained));
        Assert.All(files[2..], path => Assert.True(File.Exists(path)));
        Assert.All(files[..2], path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void Apply_MoreThanTwentyOneAcrossDays_KeepsTheNewestTwentyOne()
    {
        // Arrange
        var files = Enumerable.Range(0, 30)
            .Select(day => Create($"backup-{day:00}.balancia", new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day)))
            .ToArray();

        // Act
        var (removed, retained) = BackupRetention.Apply(directory, TimeZoneInfo.Utc);

        // Assert
        Assert.Equal((9, 21), (removed, retained));
        Assert.All(files[9..], path => Assert.True(File.Exists(path)));
        Assert.All(files[..9], path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void Apply_DaysAreCountedInTheLocalTimeZone()
    {
        // Arrange
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus12", TimeSpan.FromHours(12), "plus12", "plus12");
        // 20:00 and 23:00 UTC on 1 May are 08:00 and 11:00 on 2 May locally, the same local day as 05:00 UTC on 2 May is not.
        var sameLocalDay = new[]
        {
            Create("backup-a.balancia", new DateTime(2026, 5, 1, 13, 0, 0, DateTimeKind.Utc)),
            Create("backup-b.balancia", new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc)),
            Create("backup-c.balancia", new DateTime(2026, 5, 1, 22, 0, 0, DateTimeKind.Utc)),
            Create("backup-d.balancia", new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc)),
        };

        // Act
        var (removed, retained) = BackupRetention.Apply(directory, zone);

        // Assert
        Assert.Equal((1, 3), (removed, retained));
        Assert.False(File.Exists(sameLocalDay[0]));
    }

    [Fact]
    public void Apply_FilesOutsideThePattern_AreNeverTouched()
    {
        // Arrange
        var other = Create("notes.txt", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var snapshot = Create("Snapshot.balancia", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var (removed, retained) = BackupRetention.Apply(directory, TimeZoneInfo.Utc);

        // Assert
        Assert.Equal((0, 0), (removed, retained));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(snapshot));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
