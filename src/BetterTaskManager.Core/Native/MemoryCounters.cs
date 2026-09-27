using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// Reads the \Memory performance counters through PDH using their English names, so the lookup works on
/// German (or any other) Windows where the localized counter names differ.
/// </summary>
public sealed class MemoryCounters : IDisposable
{
    private const uint PdhFormatLarge = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedValue
    {
        public uint Status;
        public long LargeValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private readonly IntPtr query;
    private readonly Dictionary<string, IntPtr> counters = new();

    public MemoryCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return;
        foreach (string name in new[]
        {
            "Modified Page List Bytes", "Standby Cache Core Bytes", "Standby Cache Normal Priority Bytes",
            "Standby Cache Reserve Bytes", "Free & Zero Page List Bytes", "Pool Paged Resident Bytes",
            "Pool Nonpaged Bytes", "System Cache Resident Bytes", "System Driver Resident Bytes"
        })
        {
            if (PdhAddEnglishCounter(query, @"\Memory\" + name, IntPtr.Zero, out IntPtr counter) == 0) counters[name] = counter;
        }
    }

    public bool Available => query != IntPtr.Zero && counters.Count > 0;

    public Dictionary<string, long> Read()
    {
        var values = new Dictionary<string, long>();
        if (!Available || PdhCollectQueryData(query) != 0) return values;
        foreach (var (name, counter) in counters)
        {
            if (PdhGetFormattedCounterValue(counter, PdhFormatLarge, out _, out PdhFormattedValue value) == 0 && value.Status == 0)
            {
                values[name] = value.LargeValue;
            }
        }
        return values;
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }
}
