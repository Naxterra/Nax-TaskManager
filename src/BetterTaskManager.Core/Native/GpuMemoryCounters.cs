using System.Runtime.InteropServices;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Native;

/// <summary>Reads GPU process allocations and independent adapter totals through localized-safe English PDH paths.</summary>
public sealed class GpuMemoryCounters : IDisposable
{
    private readonly object sync = new();
    private IntPtr query;
    private readonly IntPtr[] counters = new IntPtr[4];
    private IntPtr buffer;
    private uint bufferSize;

    [StructLayout(LayoutKind.Sequential)]
    private struct Item { public IntPtr Name; public uint Status; public long Value; }

    public GpuMemoryCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        string[] paths = [@"\GPU Process Memory(*)\Shared Usage", @"\GPU Process Memory(*)\Dedicated Usage",
            @"\GPU Adapter Memory(*)\Shared Usage", @"\GPU Adapter Memory(*)\Dedicated Usage"];
        for (int i = 0; i < paths.Length; i++)
            if (PdhAddEnglishCounter(query, paths[i], IntPtr.Zero, out counters[i]) != 0) counters[i] = IntPtr.Zero;
    }

    public GpuMemorySample Read()
    {
        lock (sync)
        {
            if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return GpuMemorySample.Unavailable;
            return new(Read(counters[0]), Read(counters[1]), Read(counters[2]), Read(counters[3]));
        }
    }

    private Dictionary<string, long>? Read(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        uint size = bufferSize;
        uint result = PdhGetFormattedCounterArray(counter, 0x400, ref size, out uint count, buffer);
        if (result == 0x800007D2)
        {
            // PDH only guarantees a usable required size after a call with a null buffer.
            uint required = 0;
            result = PdhGetFormattedCounterArray(counter, 0x400, ref required, out _, IntPtr.Zero);
            if (result != 0x800007D2 || required == 0 || required > 32 * 1024 * 1024) return null;
            IntPtr replacement = Marshal.AllocHGlobal((int)required);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            buffer = replacement;
            bufferSize = required;
            size = bufferSize;
            result = PdhGetFormattedCounterArray(counter, 0x400, ref size, out count, buffer);
        }
        if (result != 0) return null;
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        int stride = Marshal.SizeOf<Item>();
        if (count > bufferSize / (uint)stride) return null;
        for (int i = 0; i < count; i++)
        {
            Item item = Marshal.PtrToStructure<Item>(buffer + i * stride);
            // A single exited process instance can report PDH_CSTATUS_NO_INSTANCE; skip it instead of wiping all processes.
            if (item.Status is not (0 or 1)) continue;
            if (item.Value < 0) return null;
            if (Marshal.PtrToStringUni(item.Name) is { } name && name != "_Total") values[name] = item.Value;
        }
        return values;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            bufferSize = 0;
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
