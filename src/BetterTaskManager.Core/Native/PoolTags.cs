using System.Runtime.InteropServices;
using System.Text;

namespace BetterTaskManager.Core.Native;

/// <summary>Kernel pool usage of one allocation tag (bytes currently allocated).</summary>
public readonly record struct PoolTagUsage(string Tag, long PagedBytes, long NonPagedBytes)
{
    public long TotalBytes => PagedBytes + NonPagedBytes;
}

/// <summary>
/// Kernel memory per pool tag, the same source as the WDK's poolmon. Drivers label their allocations with a
/// four-character tag; <see cref="FindDrivers"/> finds which driver files contain a tag.
/// </summary>
public static class PoolTags
{
    private const int SystemPoolTagInformation = 22;
    private const int EntrySize = 40;   // SYSTEM_POOLTAG on 64-bit Windows
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    public static List<PoolTagUsage> Read()
    {
        int size = 1 << 20;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(SystemPoolTagInformation, buffer, size, out int needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + 4096);
                    continue;
                }
                if (status < 0) return new List<PoolTagUsage>();
                int count = Marshal.ReadInt32(buffer);
                var result = new List<PoolTagUsage>(count);
                byte[] tag = new byte[4];
                for (int index = 0; index < count; index++)
                {
                    IntPtr entry = buffer + 8 + index * EntrySize;
                    Marshal.Copy(entry, tag, 0, 4);
                    result.Add(new PoolTagUsage(Encoding.ASCII.GetString(tag), Marshal.ReadInt64(entry, 16), Marshal.ReadInt64(entry, 32)));
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return new List<PoolTagUsage>();
    }

    /// <summary>
    /// Kernel files that contain each tag (the usual way to attribute a tag): every file in System32\drivers plus every
    /// kernel module that is currently loaded, which adds the Windows kernel itself and drivers installed in the driver
    /// store (for example graphics drivers).
    /// </summary>
    public static Dictionary<string, List<string>> FindDrivers(IEnumerable<string> tags)
    {
        var wanted = tags.Distinct().ToDictionary(tag => tag, tag => Encoding.ASCII.GetBytes(tag));
        var found = wanted.Keys.ToDictionary(tag => tag, _ => new List<string>());
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
        var files = new HashSet<string>(Directory.EnumerateFiles(folder, "*.sys").Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        files.UnionWith(LoadedKernelModules().Select(Path.GetFullPath));
        foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var (tag, pattern) in wanted)
            {
                string name = Path.GetFileName(file);
                if (bytes.AsSpan().IndexOf(pattern) >= 0 && !found[tag].Contains(name, StringComparer.OrdinalIgnoreCase)) found[tag].Add(name);
            }
        }
        return found;
    }

    /// <summary>
    /// Paths of the loaded kernel modules (SystemModuleInformation). Windows names them like
    /// "\SystemRoot\system32\ntoskrnl.exe" or "\??\C:\...", and only paths that resolve to an existing file are returned.
    /// </summary>
    internal static List<string> LoadedKernelModules()
    {
        const int SystemModuleInformation = 11;
        const int ModuleEntrySize = 296;   // RTL_PROCESS_MODULE_INFORMATION on 64-bit Windows
        const int FullPathOffset = 40;
        var result = new List<string>();
        int size = 1 << 18;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(SystemModuleInformation, buffer, size, out int needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + 4096);
                    continue;
                }
                if (status < 0) return result;
                int count = Marshal.ReadInt32(buffer);
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                for (int index = 0; index < count; index++)
                {
                    string? raw = Marshal.PtrToStringAnsi(buffer + 8 + index * ModuleEntrySize + FullPathOffset);
                    if (string.IsNullOrEmpty(raw)) continue;
                    string path = raw.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) ? Path.Combine(windows, raw[12..])
                        : raw.StartsWith(@"\??\", StringComparison.Ordinal) ? raw[4..]
                        : raw;
                    if (Path.IsPathFullyQualified(path) && File.Exists(path)) result.Add(path);
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return result;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int length, out int returnLength);
}
