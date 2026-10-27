using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Monitoring;

/// <summary>Which kernel pool a warning is about.</summary>
public enum KernelPoolKind
{
    NonPaged,
    Paged,
}

/// <summary>
/// A kernel pool that has grown far beyond normal, with the allocation tag that holds most of it. Kernel pool memory
/// belongs to drivers and Windows itself, so no process list shows it; a leaking driver (or a program that keeps
/// triggering one) can fill the whole RAM this way while every app looks small.
/// </summary>
public sealed record KernelPoolWarning(KernelPoolKind Pool, long PoolBytes, long ThresholdBytes, string? TopTag, long TopTagBytes);

/// <summary>Decides when kernel pool usage is abnormal and ranks the pool tags behind it.</summary>
public static class KernelPoolAlert
{
    /// <summary>The warning never fires below this size, so small machines do not warn about ordinary driver use.</summary>
    public const long MinimumThresholdBytes = 4L << 30;

    /// <summary>A pool holding this share of the physical memory is treated as abnormal.</summary>
    public const int ThresholdDivisor = 10;

    public static long ThresholdFor(long physicalTotal) => Math.Max(MinimumThresholdBytes, Math.Max(0, physicalTotal) / ThresholdDivisor);

    /// <summary>
    /// Returns a warning for the larger of the two pools when it reaches the threshold (a tenth of the RAM, at least
    /// 4 GB), naming the tag that holds most of that pool; otherwise null.
    /// </summary>
    public static KernelPoolWarning? Evaluate(long nonPagedPool, long pagedPoolResident, long physicalTotal, IReadOnlyList<PoolTagUsage> tags)
    {
        long threshold = ThresholdFor(physicalTotal);
        bool nonPagedHigh = nonPagedPool >= threshold;
        bool pagedHigh = pagedPoolResident >= threshold;
        if (!nonPagedHigh && !pagedHigh) return null;

        KernelPoolKind pool = nonPagedHigh && (!pagedHigh || nonPagedPool >= pagedPoolResident) ? KernelPoolKind.NonPaged : KernelPoolKind.Paged;
        long poolBytes = pool == KernelPoolKind.NonPaged ? nonPagedPool : pagedPoolResident;
        PoolTagUsage? top = null;
        foreach (PoolTagUsage tag in tags)
        {
            long bytes = pool == KernelPoolKind.NonPaged ? tag.NonPagedBytes : tag.PagedBytes;
            // A tag with nothing in this pool is never named, even when no tag holds anything.
            long best = top is null ? 0 : pool == KernelPoolKind.NonPaged ? top.Value.NonPagedBytes : top.Value.PagedBytes;
            if (bytes > best) top = tag;
        }
        long topBytes = top is null ? 0 : pool == KernelPoolKind.NonPaged ? top.Value.NonPagedBytes : top.Value.PagedBytes;
        return new KernelPoolWarning(pool, poolBytes, threshold, top?.Tag, topBytes);
    }

    /// <summary>The <paramref name="count"/> tags holding the most memory (paged plus non-paged), largest first.</summary>
    public static List<PoolTagUsage> Largest(IEnumerable<PoolTagUsage> tags, int count)
    {
        return tags.Where(tag => tag.TotalBytes > 0)
            .OrderByDescending(tag => tag.TotalBytes)
            .ThenBy(tag => tag.Tag, StringComparer.Ordinal)
            .Take(Math.Max(0, count))
            .ToList();
    }
}
