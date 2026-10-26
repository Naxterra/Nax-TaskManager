using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Kernel pool usage per tag for the overview and memory pages, read at most every 10 seconds (one system call), and
/// the driver files behind the largest tags. Finding a tag's driver means reading every file in System32\drivers, so it
/// runs in the background, once per tag, and the result is kept for the rest of the session.
/// </summary>
internal sealed class KernelPoolService
{
    public const int ListedTags = 8;
    private static readonly TimeSpan s_interval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, string[]> _drivers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _searching = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private DateTime _readAt = DateTime.MinValue;

    public static KernelPoolService Instance { get; } = new();

    /// <summary>The largest tags (paged plus non-paged), largest first.</summary>
    public IReadOnlyList<PoolTagUsage> Largest { get; private set; } = [];

    /// <summary>Set while a pool is far beyond normal; see <see cref="KernelPoolAlert"/>.</summary>
    public KernelPoolWarning? Warning { get; private set; }

    /// <summary>Re-reads the pool tags when the last read is older than 10 seconds. Call from the UI thread.</summary>
    public void Refresh(MemoryBreakdown memory)
    {
        if (DateTime.UtcNow - _readAt < s_interval) return;
        _readAt = DateTime.UtcNow;
        List<PoolTagUsage> tags = PoolTags.Read();
        Largest = KernelPoolAlert.Largest(tags, ListedTags);
        // Without the detailed counters the pool sizes are unknown (zero), which correctly gives no warning.
        Warning = memory.Detailed ? KernelPoolAlert.Evaluate(memory.NonpagedPool, memory.PagedPoolResident, memory.Total, tags) : null;

        var wanted = Largest.Select(tag => tag.Tag).ToList();
        if (Warning?.TopTag is { } top) wanted.Add(top);
        FindDriversInBackground(wanted);
    }

    /// <summary>The warning as full sentences for the pages, or null when the pools are normal.</summary>
    public string? WarningText()
    {
        if (Warning is not { } warning) return null;
        string pool = Loc.Get(warning.Pool == KernelPoolKind.NonPaged ? "Kernel_PoolNonPaged" : "Kernel_PoolPaged");
        string first = warning.TopTag is { } tag
            ? Loc.F("Kernel_WarningTag", pool, Format.Gigabytes(warning.PoolBytes), Format.Gigabytes(warning.TopTagBytes), tag.Trim(),
                KnownDriversOf(tag) is { } drivers ? " (" + drivers + ")" : "")
            : Loc.F("Kernel_WarningPool", pool, Format.Gigabytes(warning.PoolBytes));
        return first + " " + Loc.Get("Kernel_WarningExplain");
    }

    /// <summary>"ntfs.sys", several files joined, or a short note while the search runs or when nothing was found.</summary>
    public string DriversOf(string tag)
    {
        lock (_lock)
        {
            if (_drivers.TryGetValue(tag, out string[]? files))
            {
                if (files.Length == 0) return Loc.Get("Kernel_DriverUnknown");
                return files.Length <= 3 ? string.Join(", ", files) : string.Join(", ", files.Take(3)) + Loc.F("Kernel_DriverMore", files.Length - 3);
            }
            return Loc.Get("Kernel_DriverSearching");
        }
    }

    /// <summary>The driver names for a tag once known, otherwise null.</summary>
    public string? KnownDriversOf(string tag)
    {
        lock (_lock)
        {
            return _drivers.TryGetValue(tag, out string[]? files) && files.Length > 0 ? string.Join(", ", files.Take(3)) : null;
        }
    }

    private void FindDriversInBackground(IEnumerable<string> tags)
    {
        List<string> missing;
        lock (_lock)
        {
            missing = tags.Where(tag => !_drivers.ContainsKey(tag) && !_searching.Contains(tag)).Distinct().ToList();
            if (missing.Count == 0) return;
            foreach (string tag in missing) _searching.Add(tag);
        }
        Task.Run(() =>
        {
            Dictionary<string, List<string>> found;
            try
            {
                found = PoolTags.FindDrivers(missing);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                found = missing.ToDictionary(tag => tag, _ => new List<string>());
            }
            lock (_lock)
            {
                foreach (string tag in missing)
                {
                    _drivers[tag] = found.TryGetValue(tag, out List<string>? files) ? [.. files] : [];
                    _searching.Remove(tag);
                }
            }
        });
    }
}
