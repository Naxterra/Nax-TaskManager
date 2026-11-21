using System.Runtime.InteropServices;
using System.Text;

namespace BetterTaskManager.Core.Native;

/// <summary>Converts kernel paths ("\Device\HarddiskVolume3\Windows\…") to drive-letter paths.</summary>
public static class DevicePaths
{
    private static readonly object gate = new();
    private static Dictionary<string, string>? devices;
    private static long readAt;

    public static string ToDosPath(string kernelPath)
    {
        if (string.IsNullOrEmpty(kernelPath) || !kernelPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return kernelPath;
        Dictionary<string, string>? snapshot = Volatile.Read(ref devices);
        long last = Interlocked.Read(ref readAt);
        // Drives come and go (USB, VHD); re-read the mapping at most once a minute.
        if (snapshot is null || System.Diagnostics.Stopwatch.GetElapsedTime(last) > TimeSpan.FromMinutes(1))
        {
            lock (gate)
            {
                snapshot = devices;
                if (snapshot is null || System.Diagnostics.Stopwatch.GetElapsedTime(readAt) > TimeSpan.FromMinutes(1))
                {
                    snapshot = ReadDevices();
                    Volatile.Write(ref devices, snapshot);
                    Interlocked.Exchange(ref readAt, System.Diagnostics.Stopwatch.GetTimestamp());
                }
            }
        }
        foreach (var (device, drive) in snapshot)
        {
            if (kernelPath.Length > device.Length && kernelPath[device.Length] == '\\' &&
                kernelPath.StartsWith(device, StringComparison.OrdinalIgnoreCase))
            {
                return drive + kernelPath[device.Length..];
            }
        }
        return kernelPath;
    }

    private static Dictionary<string, string> ReadDevices()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var target = new StringBuilder(1024);
        foreach (string drive in Environment.GetLogicalDrives())
        {
            string letter = drive.TrimEnd('\\');
            if (QueryDosDevice(letter, target, target.Capacity) > 0) map[target.ToString()] = letter;
        }
        return map;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int max);
}
