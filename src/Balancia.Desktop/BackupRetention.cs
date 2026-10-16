namespace Balancia.Desktop;

/// <summary>Chooses which rolling backups to keep: the newest few per local calendar day, and a fixed number overall.</summary>
internal static class BackupRetention
{
    private const int MaxRetainedBackups = 21;
    private const int MaxDailyBackups = 3;
    internal const string Pattern = "backup-*.balancia";

    /// <summary>Deletes the backups outside the policy and returns how many were removed and kept.</summary>
    public static (int Removed, int Retained) Apply(string directory, TimeZoneInfo localZone)
    {
        var backups = Directory.EnumerateFiles(directory, Pattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var retained = backups
            .GroupBy(file => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(file.LastWriteTimeUtc, localZone)))
            .SelectMany(day => day.Take(MaxDailyBackups))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(MaxRetainedBackups)
            .ToHashSet();
        foreach (var old in backups.Where(file => !retained.Contains(file)))
        {
            old.Delete();
        }

        return (backups.Length - retained.Count, retained.Count);
    }
}
