using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>Maps process IDs to the display names of the Windows services they host.</summary>
public static class ServiceNames
{
    private const uint ScManagerEnumerateService = 0x0004;
    private const int ScEnumProcessInfo = 0;
    private const uint ServiceWin32 = 0x30;
    private const uint ServiceActive = 0x1;
    private const int ErrorMoreData = 234;
    private const int EntrySize = 56;
    private const int OffsetDisplayName = 8;
    private const int OffsetProcessId = 44;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumServicesStatusEx(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr services, int bufferSize, out int bytesNeeded, out int servicesReturned, ref int resumeHandle, string? groupName);

    public static Dictionary<int, List<string>> ReadByProcess()
    {
        var result = new Dictionary<int, List<string>>();
        IntPtr manager = OpenSCManager(null, null, ScManagerEnumerateService);
        if (manager == IntPtr.Zero) return result;
        try
        {
            int resume = 0;
            int size = 256 * 1024;
            while (true)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    bool ok = EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceActive, buffer, size,
                        out int needed, out int returned, ref resume, null);
                    int error = ok ? 0 : Marshal.GetLastWin32Error();
                    if (!ok && error != ErrorMoreData) return result;

                    for (int index = 0; index < returned; index++)
                    {
                        IntPtr entry = buffer + index * EntrySize;
                        int pid = Marshal.ReadInt32(entry, OffsetProcessId);
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(entry, OffsetDisplayName)) ?? "";
                        if (pid == 0 || name.Length == 0) continue;
                        if (!result.TryGetValue(pid, out List<string>? names)) result[pid] = names = new List<string>();
                        names.Add(name);
                    }

                    if (ok) return result;
                    size = Math.Max(size, needed + 4096);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }
}
