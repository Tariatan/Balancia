namespace Balancia.Desktop.Tests;

/// <summary>A clock the test moves by hand, in UTC so "local" days do not depend on the machine time zone.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan duration) => now += duration;
}
