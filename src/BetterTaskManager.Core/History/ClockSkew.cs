namespace BetterTaskManager.Core.History;

/// <summary>
/// Notices the wall clock jumping while the service runs, by comparing how far it moved with the tick count (which
/// keeps counting through sleep and hibernation and is never set). A clock set ahead by mistake adds to the skew, and
/// setting it back again takes the skew off. Ordinary drift and time-server corrections stay below the threshold.
/// </summary>
public static class ClockSkew
{
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

    /// <summary>The skew after the wall clock moved <paramref name="wallElapsed"/> while real time moved <paramref name="realElapsed"/>.</summary>
    public static TimeSpan Update(TimeSpan skew, TimeSpan wallElapsed, TimeSpan realElapsed)
    {
        TimeSpan jump = wallElapsed - realElapsed;
        if (jump.Duration() < Threshold) return skew;
        TimeSpan updated = skew + jump;
        return updated < TimeSpan.Zero ? TimeSpan.Zero : updated;
    }
}
