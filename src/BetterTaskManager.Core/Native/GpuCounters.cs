using System.Globalization;
using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>GPU use of one process: its busiest engine, as Task Manager's GPU and GPU engine columns show it.</summary>
public readonly record struct ProcessGpu(double Percent, string Engine);

/// <summary>
/// Reads the "GPU Engine" performance counters (one instance per process and engine, named like
/// <c>pid_1234_luid_0x0_0xD1B7_phys_0_eng_3_engtype_3D</c>) through PDH. Per process the busiest engine counts;
/// for the whole GPU the busiest engine summed over all processes, the same rules Task Manager uses.
/// </summary>
public sealed class GpuCounters : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedItem
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private readonly object sync = new();
    private IntPtr query;
    private readonly IntPtr counter;
    private IntPtr buffer;
    private uint bufferSize;
    private bool primed;
    private readonly Dictionary<string, int> luidIndices = new(StringComparer.OrdinalIgnoreCase);

    public GpuCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) != 0)
        {
            PdhCloseQuery(query);
            query = IntPtr.Zero;
            return;
        }
    }

    public bool Available => query != IntPtr.Zero;

    /// <summary>Busiest engine per process since the previous call, plus the busiest engine of the whole GPU.</summary>
    public (Dictionary<int, ProcessGpu> ByProcess, double Total) Read()
    {
        lock (sync) return ReadCore();
    }

    private (Dictionary<int, ProcessGpu> ByProcess, double Total) ReadCore()
    {
        var byProcess = new Dictionary<int, ProcessGpu>();
        if (!Available || PdhCollectQueryData(query) != 0) return (byProcess, 0);
        // The first collect establishes the PDH rate baseline; wait until the next tick to avoid a sub-millisecond spike.
        if (!primed)
        {
            primed = true;
            return (byProcess, 0);
        }

        uint sizeForCall = bufferSize;
        uint result = PdhGetFormattedCounterArray(counter, PdhFormatDouble | PdhFormatNoCap100, ref sizeForCall, out uint items, buffer);
        if (result == PdhMoreData)
        {
            // PDH only guarantees a usable required size after a call with a null buffer.
            uint required = 0;
            result = PdhGetFormattedCounterArray(counter, PdhFormatDouble | PdhFormatNoCap100, ref required, out _, IntPtr.Zero);
            if (result != PdhMoreData || required == 0 || required > int.MaxValue) return (byProcess, 0);
            IntPtr replacement = Marshal.AllocHGlobal((int)required);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            buffer = replacement;
            bufferSize = required;
            sizeForCall = bufferSize;
            result = PdhGetFormattedCounterArray(counter, PdhFormatDouble | PdhFormatNoCap100, ref sizeForCall, out items, buffer);
        }
        if (result != 0) return (byProcess, 0);

        int size = Marshal.SizeOf<PdhFormattedItem>();
        if (items > bufferSize / (uint)size) return (byProcess, 0);
        var perEngine = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < items; i++)
        {
            var item = Marshal.PtrToStructure<PdhFormattedItem>(buffer + i * size);
            if (item.Status != 0 || item.Value <= 0) continue;
            string? name = Marshal.PtrToStringUni(item.Name);
            if (name is null || !TryParse(name, out int pid, out string engineKey, out string engine, ResolveGpuIndex)) continue;
            double value = Math.Min(item.Value, 100);
            perEngine[engineKey] = perEngine.GetValueOrDefault(engineKey) + value;
            if (!byProcess.TryGetValue(pid, out ProcessGpu current) || value > current.Percent) byProcess[pid] = new ProcessGpu(value, engine);
        }
        double total = perEngine.Count == 0 ? 0 : Math.Min(100, perEngine.Values.Max());
        return (byProcess, total);
    }

    private int ResolveGpuIndex(string luidTag)
    {
        if (luidIndices.TryGetValue(luidTag, out int index)) return index;
        index = luidIndices.Count;
        luidIndices[luidTag] = index;
        return index;
    }

    /// <summary>Splits <c>pid_1234_luid_0x0_0xD1B7_phys_0_eng_3_engtype_VideoDecode</c>, numbering distinct WDDM LUIDs separately.</summary>
    internal static bool TryParse(string instance, out int pid, out string engineKey, out string engine, Func<string, int>? gpuIndexForLuid = null)
    {
        pid = 0;
        engineKey = engine = "";
        if (!instance.StartsWith("pid_", StringComparison.Ordinal)) return false;
        int luid = instance.IndexOf("_luid_", StringComparison.Ordinal);
        int phys = instance.IndexOf("_phys_", StringComparison.Ordinal);
        int type = instance.IndexOf("_engtype_", StringComparison.Ordinal);
        if (luid < 0 || phys < luid || type < phys) return false;
        if (!int.TryParse(instance.AsSpan(4, luid - 4), NumberStyles.None, CultureInfo.InvariantCulture, out pid)) return false;
        engineKey = instance[luid..type];
        string luidTag = instance[(luid + 6)..phys];
        string gpu;
        if (gpuIndexForLuid is not null && luidTag.Length > 0)
        {
            gpu = gpuIndexForLuid(luidTag).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            int physEnd = instance.IndexOf('_', phys + 6);
            gpu = physEnd < 0 ? "0" : instance[(phys + 6)..physEnd];
        }
        string typeName = instance[(type + 9)..];
        engine = $"GPU {gpu} - {(typeName.Length == 0 ? "?" : typeName)}";
        return true;
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
}
