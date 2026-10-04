using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>Outcome of trimming every process's working set.</summary>
public readonly record struct TrimResult(int Trimmed, int Denied, int Exited, int Failed);

/// <summary>Outcome of a system-wide memory list command.</summary>
public readonly record struct CleanupResult(bool Succeeded, string Message);

/// <summary>
/// The memory cleanup actions of the classic app: trim working sets, purge the standby list and empty the system
/// working sets. These are troubleshooting tools: Windows uses spare RAM as cache on purpose, and trimmed pages move
/// to the standby or modified list, from where apps fault them back in.
/// </summary>
public static class MemoryCleanup
{
    private const int ProcessSetQuota = 0x0100;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryPurgeStandbyList = 4;
    private const uint StatusPrivilegeNotHeld = 0xC0000061;
    private const uint StatusAccessDenied = 0xC0000022;

    /// <summary>
    /// Empties the working set of every process this account may open (except PID 0/4 and <paramref name="excludePid"/>).
    /// Works without elevation for the user's own processes; elevation reaches most services.
    /// </summary>
    public static TrimResult TrimAllWorkingSets(int excludePid)
    {
        int trimmed = 0, denied = 0, exited = 0, failed = 0;
        foreach (RawProcess process in NtProcessReader.Read())
        {
            if (process.Pid <= 4 || process.Pid == excludePid) continue;
            IntPtr handle = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation | ProcessSetQuota, false, process.Pid);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 5) denied++;
                else if (error == 87) exited++;
                else failed++;
                continue;
            }
            try
            {
                if (EmptyWorkingSet(handle)) trimmed++;
                else if (Marshal.GetLastWin32Error() is 5 or 1314) denied++;
                else failed++;
            }
            finally
            {
                Win32.CloseHandle(handle);
            }
        }
        return new TrimResult(trimmed, denied, exited, failed);
    }

    /// <summary>Moves standby pages to the free list. Needs administrator rights.</summary>
    public static CleanupResult PurgeStandbyList() => MemoryListCommand(MemoryPurgeStandbyList, "Standby cache cleared.");

    /// <summary>Trims every working set, including system ones, in one kernel call. Needs administrator rights.</summary>
    public static CleanupResult EmptySystemWorkingSets() => MemoryListCommand(MemoryEmptyWorkingSets, "All working sets emptied.");

    private static CleanupResult MemoryListCommand(int command, string success)
    {
        if (!TryEnablePrivilege("SeProfileSingleProcessPrivilege", out int privilegeError))
        {
            return new CleanupResult(false, privilegeError == 1300
                ? "Windows did not grant the \"Profile single process\" privilege to this process. This needs administrator rights."
                : $"Could not enable the \"Profile single process\" privilege (error {privilegeError}).");
        }
        TryEnablePrivilege("SeIncreaseQuotaPrivilege", out _);

        uint status = (uint)NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        return status switch
        {
            0 => new CleanupResult(true, success),
            StatusPrivilegeNotHeld => new CleanupResult(false, "Windows refused: the required privilege is not held. This needs administrator rights."),
            StatusAccessDenied => new CleanupResult(false, "Windows refused the request (access denied)."),
            _ => new CleanupResult(false, $"Windows returned status 0x{status:X8}.")
        };
    }

    private static bool TryEnablePrivilege(string name, out int error)
    {
        error = 0;
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            bool adjusted = AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
            // AdjustTokenPrivileges "succeeds" with ERROR_NOT_ALL_ASSIGNED (1300) when the token lacks the privilege.
            error = Marshal.GetLastWin32Error();
            return adjusted && error == 0;
        }
        finally
        {
            Win32.CloseHandle(token);
        }
    }

    private const int TokenAdjustPrivileges = 0x0020;
    private const int TokenQuery = 0x0008;
    private const int SePrivilegeEnabled = 0x0002;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int informationClass, ref int information, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returnLength);
}
