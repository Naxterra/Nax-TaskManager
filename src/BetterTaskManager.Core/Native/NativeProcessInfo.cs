namespace BetterTaskManager.Core.Native;

/// <summary>Public process queries for the UI layer.</summary>
public static class NativeProcessInfo
{
    /// <summary>True when Windows marks the process critical: ending it crashes or restarts Windows.</summary>
    public static bool IsCritical(int pid) => pid is > 0 and <= 4 || Win32.IsCritical(pid);
}
