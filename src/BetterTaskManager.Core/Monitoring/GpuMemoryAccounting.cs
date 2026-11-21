using System.Globalization;

namespace BetterTaskManager.Core.Monitoring;

public readonly record struct ProcessGpuMemory(long? Shared, long? Dedicated);

/// <summary>GPU allocations are separate from CPU working sets and commit; they must not be added to either.</summary>
public sealed class GpuMemorySample
{
    public static GpuMemorySample Unavailable { get; } = new(null, null, null, null);
    private readonly Dictionary<int, long>? shared, dedicated;
    public long? SharedTotal { get; }
    public long? DedicatedTotal { get; }

    public GpuMemorySample(IReadOnlyDictionary<string, long>? processShared, IReadOnlyDictionary<string, long>? processDedicated,
        IReadOnlyDictionary<string, long>? adapterShared, IReadOnlyDictionary<string, long>? adapterDedicated)
    {
        shared = GroupProcesses(processShared);
        dedicated = GroupProcesses(processDedicated);
        SharedTotal = Total(adapterShared);
        DedicatedTotal = Total(adapterDedicated);
    }

    public ProcessGpuMemory ForProcess(int pid) => new(shared?.GetValueOrDefault(pid), dedicated?.GetValueOrDefault(pid));

    private static long? Total(IReadOnlyDictionary<string, long>? values) =>
        values is null || values.Values.Any(value => value < 0) ? null : values.Values.Sum();

    private static Dictionary<int, long>? GroupProcesses(IReadOnlyDictionary<string, long>? values)
    {
        if (values is null || values.Values.Any(value => value < 0)) return null;
        var result = new Dictionary<int, long>();
        foreach (var (instance, bytes) in values)
        {
            int end = instance.IndexOf("_luid_", StringComparison.Ordinal);
            if (!instance.StartsWith("pid_", StringComparison.Ordinal) || end <= 4
                || !int.TryParse(instance.AsSpan(4, end - 4), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0) continue;
            result[pid] = result.GetValueOrDefault(pid) + bytes;
        }
        return result;
    }
}

/// <summary>Independent process memory measures, retaining unavailable GPU samples through grouping.</summary>
public readonly record struct ProcessMemoryUsage(long Resident, long Committed, long? GpuShared, long? GpuDedicated)
{
    public static ProcessMemoryUsage Sum(IEnumerable<ProcessSample> processes)
    {
        long resident = 0, committed = 0;
        long? shared = 0, dedicated = 0;
        foreach (var process in processes)
        {
            resident += process.PrivateWorkingSet;
            committed += process.CommitCharge;
            shared += process.GpuSharedMemory;
            dedicated += process.GpuDedicatedMemory;
        }
        return new(resident, committed, shared, dedicated);
    }
}
