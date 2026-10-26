namespace Balancia.Storage;

/// <summary>Keeps the newest three backups per local calendar day for 180 days, up to 540 overall.</summary>
internal static class BackupRetention
{
    private const int RetentionDays = 180;
    private const int MaxDailyBackups = 3;
    private const int MaxRetainedBackups = RetentionDays * MaxDailyBackups;
    internal const string Pattern = "backup-*.balancia";

    /// <summary>Deletes the backups outside the policy and returns how many were removed and kept.</summary>
    public static (int Removed, int Retained) Apply(string directory, TimeZoneInfo localZone, DateOnly today)
    {
        var backups = Directory.EnumerateFiles(directory, Pattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var retained = backups
            .GroupBy(file => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(file.LastWriteTimeUtc, localZone)))
            .Where(day => day.Key >= today.AddDays(1 - RetentionDays))
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
