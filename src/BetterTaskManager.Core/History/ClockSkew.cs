namespace BetterTaskManager.Core.History;

/// <summary>
/// How much of the wall clock's progress pruning does not trust yet. Pruning subtracts it from the current time, so a
/// clock set a year ahead by mistake cannot delete the whole history at once.
/// <list type="bullet">
/// <item>While the service runs, a jump is measured exactly against the tick count (which keeps counting through
/// sleep and is never set). Ordinary drift and time-server corrections stay below <see cref="Threshold"/>.</item>
/// <item>While the service was off, real time cannot be measured. A gap of up to <see cref="TrustedDowntime"/> is
/// believed, and the rest is held back like a jump.</item>
/// <item>The held-back time is released at <see cref="ReleaseRate"/> times real time, because a jump can also be a
/// correct clock catching up (after a dead CMOS battery or a restored virtual machine). Pruning then resumes after
/// a quarter of the jump has passed in real time, while a mistaken jump costs no history younger than the retention
/// period for that long.</item>
/// </list>
/// </summary>
public static class ClockSkew
{
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TrustedDowntime = TimeSpan.FromDays(3);
    public const int ReleaseRate = 4;

    /// <summary>The held-back time after the wall clock moved <paramref name="wallElapsed"/> while real time moved <paramref name="realElapsed"/>.</summary>
    public static TimeSpan Update(TimeSpan skew, TimeSpan wallElapsed, TimeSpan realElapsed)
    {
        TimeSpan jump = wallElapsed - realElapsed;
        if (jump.Duration() >= Threshold) skew += jump;
        return Release(skew, realElapsed);
    }

    /// <summary>The held-back time after the service was off while the wall clock moved <paramref name="gap"/>.</summary>
    public static TimeSpan AfterDowntime(TimeSpan skew, TimeSpan gap) =>
        gap > TrustedDowntime ? skew + (gap - TrustedDowntime) : skew;

    private static TimeSpan Release(TimeSpan skew, TimeSpan realElapsed)
    {
        if (realElapsed > TimeSpan.Zero) skew -= ReleaseRate * realElapsed;
        return skew < TimeSpan.Zero ? TimeSpan.Zero : skew;
    }
}
