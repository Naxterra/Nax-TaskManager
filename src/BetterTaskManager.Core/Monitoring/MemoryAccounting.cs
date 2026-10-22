namespace BetterTaskManager.Core.Monitoring;

/// <summary>Builds one physical-memory sample without treating a missing counter as a measured zero.</summary>
public static class MemoryAccounting
{
    private static readonly string[] PageLists = ["Standby Cache Core Bytes", "Standby Cache Normal Priority Bytes",
        "Standby Cache Reserve Bytes", "Free & Zero Page List Bytes"];
    private static readonly string[] Attribution = ["Pool Paged Resident Bytes", "Pool Nonpaged Bytes",
        "System Cache Resident Bytes", "System Driver Resident Bytes"];

    public static MemoryBreakdown Create(long total, long available, long commit, long commitLimit,
        long processPrivate, IReadOnlyDictionary<string, long> counters)
    {
        bool Has(string key) => counters.TryGetValue(key, out long value) && value >= 0;
        long Get(string key) => counters.TryGetValue(key, out long value) ? Math.Max(0, value) : 0;
        total = Math.Max(0, total);
        available = Math.Clamp(available, 0, total);
        bool modifiedAvailable = Has("Modified Page List Bytes");
        long modified = Math.Min(Get("Modified Page List Bytes"), total - available);
        return new MemoryBreakdown
        {
            Total = total, Available = available, InUse = total - available - modified,
            Modified = modified, ModifiedAvailable = modifiedAvailable,
            Standby = Get(PageLists[0]) + Get(PageLists[1]) + Get(PageLists[2]), Free = Get(PageLists[3]),
            PageListsAvailable = PageLists.All(Has), Detailed = modifiedAvailable && Attribution.All(Has),
            ProcessPrivate = Math.Max(0, processPrivate),
            PagedPoolResident = Get(Attribution[0]), NonpagedPool = Get(Attribution[1]),
            KernelPools = Get(Attribution[0]) + Get(Attribution[1]),
            FileCache = Get(Attribution[2]), Drivers = Get(Attribution[3]),
            CommitTotal = Math.Max(0, commit), CommitLimit = Math.Max(0, commitLimit)
        };
    }
}
