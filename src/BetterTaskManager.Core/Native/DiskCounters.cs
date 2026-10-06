using System.Globalization;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Monitoring;
using Microsoft.Win32.SafeHandles;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// Per physical disk: active time, throughput, response time and queue length from the "PhysicalDisk" performance
/// counters (English names, so German Windows works too), free space of its volumes, and the model and bus from the
/// storage driver. None of this needs administrator rights.
/// </summary>
public sealed class DiskCounters : IDisposable
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

    private static readonly string[] CounterNames =
        ["% Idle Time", "Disk Read Bytes/sec", "Disk Write Bytes/sec", "Avg. Disk sec/Transfer", "Current Disk Queue Length"];

    private readonly IntPtr query;
    private readonly IntPtr[] counters = new IntPtr[CounterNames.Length];
    private readonly Dictionary<int, (string Model, string Kind)> devices = new();
    private readonly Dictionary<string, (string Label, long Free, long Total)> volumes = new(StringComparer.OrdinalIgnoreCase);
    private long volumesReadAt;

    public DiskCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        for (int index = 0; index < CounterNames.Length; index++)
        {
            if (PdhAddEnglishCounter(query, @"\PhysicalDisk(*)\" + CounterNames[index], IntPtr.Zero, out counters[index]) != 0)
            {
                PdhCloseQuery(query);
                query = IntPtr.Zero;
                return;
            }
        }
        PdhCollectQueryData(query);
    }

    public bool Available => query != IntPtr.Zero;

    public List<DiskSample> Read()
    {
        var result = new List<DiskSample>();
        if (!Available || PdhCollectQueryData(query) != 0) return result;

        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        for (int index = 0; index < counters.Length; index++)
        {
            foreach (var (instance, value) in ReadArray(counters[index]))
            {
                if (instance == "_Total") continue;
                if (!values.TryGetValue(instance, out double[]? row)) values[instance] = row = new double[CounterNames.Length];
                row[index] = value;
            }
        }

        bool refreshVolumes = volumesReadAt == 0 || Environment.TickCount64 - volumesReadAt > 10_000;
        if (refreshVolumes) volumesReadAt = Environment.TickCount64;
        foreach (var (instance, row) in values)
        {
            // Instances are "<disk number> <drive letters>", for example "0 C:" or "1 D: E:".
            string[] parts = instance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int number)) continue;
            var diskVolumes = new List<VolumeSample>();
            foreach (string letter in parts.Skip(1))
            {
                if (refreshVolumes || !volumes.ContainsKey(letter)) volumes[letter] = ReadVolume(letter);
                var (label, free, total) = volumes[letter];
                diskVolumes.Add(new VolumeSample(letter, label, free, total));
            }
            if (!devices.TryGetValue(number, out var device)) devices[number] = device = QueryDevice(number);
            result.Add(new DiskSample(number, device.Model, device.Kind, diskVolumes,
                ActivePercent: Math.Clamp(100 - row[0], 0, 100),
                ReadPerSecond: Math.Max(0, row[1]),
                WritePerSecond: Math.Max(0, row[2]),
                ResponseMilliseconds: Math.Max(0, row[3] * 1000),
                QueueLength: Math.Max(0, row[4])));
        }
        result.Sort((left, right) => left.Number.CompareTo(right.Number));
        return result;
    }

    private IEnumerable<(string Instance, double Value)> ReadArray(IntPtr counter)
    {
        uint size = 0;
        uint result = PdhGetFormattedCounterArray(counter, PdhFormatDouble | PdhFormatNoCap100, ref size, out uint items, IntPtr.Zero);
        if (result != PdhMoreData || size == 0) yield break;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PdhFormatDouble | PdhFormatNoCap100, ref size, out items, buffer) != 0) yield break;
            int itemSize = Marshal.SizeOf<PdhFormattedItem>();
            for (int index = 0; index < items; index++)
            {
                var item = Marshal.PtrToStructure<PdhFormattedItem>(buffer + index * itemSize);
                if (item.Status != 0) continue;
                yield return (Marshal.PtrToStringUni(item.Name) ?? "", item.Value);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string Label, long Free, long Total) ReadVolume(string letter)
    {
        try
        {
            var drive = new DriveInfo(letter);
            return drive.IsReady ? (drive.VolumeLabel, drive.AvailableFreeSpace, drive.TotalSize) : ("", 0, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ("", 0, 0);
        }
    }

    // ===== Model and bus type through IOCTL_STORAGE_QUERY_PROPERTY (works with no access rights on the device) =====

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    private static (string Model, string Kind) QueryDevice(int number)
    {
        try
        {
            using SafeFileHandle handle = CreateFile(@"\\.\PhysicalDrive" + number.ToString(CultureInfo.InvariantCulture), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) return ("", "");

            byte[] output = new byte[1024];
            string model = "", bus = "";
            if (DeviceIoControl(handle, IoctlStorageQueryProperty, Query(StorageDeviceProperty), 12, output, output.Length, out int returned, IntPtr.Zero) && returned >= 36)
            {
                // STORAGE_DEVICE_DESCRIPTOR: VendorIdOffset at 12, ProductIdOffset at 16, BusType at 28.
                string vendor = AnsiAt(output, BitConverter.ToInt32(output, 12), returned);
                string product = AnsiAt(output, BitConverter.ToInt32(output, 16), returned);
                model = vendor.Length > 0 && !product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ? vendor + " " + product : product;
                bus = BitConverter.ToInt32(output, 28) switch
                {
                    17 => "NVMe",
                    11 => "SATA",
                    7 => "USB",
                    10 => "SAS",
                    8 => "RAID",
                    14 or 15 => "Virtual",
                    16 => "Storage Spaces",
                    _ => ""
                };
            }

            string media = "";
            if (DeviceIoControl(handle, IoctlStorageQueryProperty, Query(StorageDeviceSeekPenaltyProperty), 12, output, output.Length, out returned, IntPtr.Zero) && returned >= 9)
            {
                // DEVICE_SEEK_PENALTY_DESCRIPTOR: IncursSeekPenalty at 8. Spinning disks pay a seek penalty, SSDs do not.
                media = output[8] != 0 ? "HDD" : "SSD";
            }
            string kind = media.Length > 0 && bus.Length > 0 ? $"{media} ({bus})" : media + bus;
            return (model, kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("", "");
        }
    }

    private static byte[] Query(int propertyId)
    {
        // STORAGE_PROPERTY_QUERY: PropertyId, QueryType = PropertyStandardQuery (0), AdditionalParameters.
        byte[] query = new byte[12];
        BitConverter.TryWriteBytes(query, propertyId);
        return query;
    }

    private static string AnsiAt(byte[] buffer, int offset, int length)
    {
        if (offset <= 0 || offset >= length) return "";
        int end = Array.IndexOf(buffer, (byte)0, offset);
        if (end < 0 || end > length) end = length;
        return System.Text.Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }
}
