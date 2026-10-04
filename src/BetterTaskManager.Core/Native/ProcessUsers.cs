using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// The account each process runs as, from one WTSEnumerateProcessesEx call. Windows only reports the owner of
/// another account's process to an elevated caller; those processes are missing from the result otherwise.
/// </summary>
public static class ProcessUsers
{
    private const uint WtsAnySession = 0xFFFFFFFE;
    private const int WtsTypeProcessInfoLevel1 = 1;
    private static readonly Dictionary<string, string> s_names = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsProcessInfoEx
    {
        public int SessionId;
        public int ProcessId;
        public IntPtr ProcessName;
        public IntPtr UserSid;
        public int NumberOfThreads;
        public int HandleCount;
        public int PagefileUsage;
        public int PeakPagefileUsage;
        public int WorkingSetSize;
        public int PeakWorkingSetSize;
        public long UserTime;
        public long KernelTime;
    }

    /// <summary>PID → account name without the domain ("Naxterra", "SYSTEM", "LOCAL SERVICE" in the Windows language).</summary>
    public static Dictionary<int, string> Read()
    {
        var result = new Dictionary<int, string>();
        int level = 1;
        if (!WTSEnumerateProcessesEx(IntPtr.Zero, ref level, WtsAnySession, out IntPtr buffer, out int count)) return result;
        try
        {
            int size = Marshal.SizeOf<WtsProcessInfoEx>();
            for (int index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<WtsProcessInfoEx>(buffer + index * size);
                if (info.UserSid != IntPtr.Zero) result[info.ProcessId] = Name(new SecurityIdentifier(info.UserSid));
            }
        }
        finally
        {
            WTSFreeMemoryEx(WtsTypeProcessInfoLevel1, buffer, count);
        }
        return result;
    }

    private static string Name(SecurityIdentifier sid)
    {
        string key = sid.Value;
        lock (s_names)
        {
            if (s_names.TryGetValue(key, out string? cached)) return cached;
        }
        string name;
        try
        {
            name = sid.Translate(typeof(NTAccount)).Value;
            int slash = name.IndexOf('\\');
            if (slash >= 0) name = name[(slash + 1)..];
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            name = key;
        }
        lock (s_names) s_names[key] = name;
        return name;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateProcessesEx(IntPtr server, ref int level, uint sessionId, out IntPtr processInfo, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSFreeMemoryEx(int type, IntPtr memory, int count);
}
