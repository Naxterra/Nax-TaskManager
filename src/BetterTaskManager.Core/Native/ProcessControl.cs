using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>Windows priority classes (the values GetPriorityClass returns).</summary>
public enum PriorityClass
{
    Unknown = 0,
    Idle = 0x40,
    BelowNormal = 0x4000,
    Normal = 0x20,
    AboveNormal = 0x8000,
    High = 0x80,
    Realtime = 0x100
}

public readonly record struct ProcessControlState(PriorityClass Priority, bool Efficiency);

/// <summary>Outcome of a change: <see cref="Gone"/> when the process exited or its PID now belongs to another process.</summary>
public readonly record struct ControlResult(bool Succeeded, bool Gone, int Error)
{
    public static ControlResult Ok => new(true, false, 0);
}

/// <summary>
/// Priority and efficiency mode, the two process settings Task Manager can change. Efficiency mode is what Task
/// Manager does: EcoQoS (execution-speed power throttling) plus idle priority.
/// </summary>
public static class ProcessControl
{
    private const int ProcessSetInformation = 0x0200;
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>Reads both settings with one limited handle; false when the process cannot be opened.</summary>
    public static bool TryRead(int pid, out ProcessControlState state)
    {
        state = default;
        IntPtr handle = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var priority = (PriorityClass)GetPriorityClass(handle);
            var throttling = new PowerThrottlingState { Version = PowerThrottlingCurrentVersion };
            bool efficiency = GetProcessInformation(handle, ProcessPowerThrottling, ref throttling, Marshal.SizeOf<PowerThrottlingState>()) &&
                (throttling.ControlMask & PowerThrottlingExecutionSpeed) != 0 && (throttling.StateMask & PowerThrottlingExecutionSpeed) != 0;
            state = new ProcessControlState(priority, efficiency);
            return true;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>
    /// On: EcoQoS and idle priority. Off: explicitly no EcoQoS (so Windows' own heuristics do not put it back
    /// straight away) and normal priority, as Task Manager does.
    /// </summary>
    public static ControlResult SetEfficiency(int pid, long createTime, bool on) => WithProcess(pid, createTime, handle =>
    {
        var throttling = new PowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = on ? PowerThrottlingExecutionSpeed : 0
        };
        if (!SetProcessInformation(handle, ProcessPowerThrottling, ref throttling, Marshal.SizeOf<PowerThrottlingState>())) return false;
        return SetPriorityClass(handle, (uint)(on ? PriorityClass.Idle : PriorityClass.Normal));
    });

    public static ControlResult SetPriority(int pid, long createTime, PriorityClass priority) =>
        WithProcess(pid, createTime, handle => SetPriorityClass(handle, (uint)priority));

    /// <summary>Opens the process and checks its creation time first, so a reused PID is never changed.</summary>
    private static ControlResult WithProcess(int pid, long createTime, Func<IntPtr, bool> change)
    {
        IntPtr handle = Win32.OpenProcess(ProcessSetInformation | Win32.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorInvalidParameter ? new ControlResult(false, true, error) : new ControlResult(false, false, error);
        }
        try
        {
            if (!GetProcessTimes(handle, out long created, out _, out _, out _) || created != createTime) return new ControlResult(false, true, 0);
            return change(handle) ? ControlResult.Ok : new ControlResult(false, false, Marshal.GetLastWin32Error());
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    private const int ErrorInvalidParameter = 87;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
}
