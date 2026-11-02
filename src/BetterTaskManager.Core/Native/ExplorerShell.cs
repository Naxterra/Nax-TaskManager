using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// Restarting Windows Explorer, the shell that draws the taskbar and the desktop. Ending it is enough in most cases,
/// because Winlogon starts the shell again when it exits with an error code (AutoRestartShell). When it does not
/// come back, the app starts it itself, never elevated: an Explorer started from an elevated app would run the
/// whole shell, and everything opened from it, with administrator rights.
/// </summary>
public static class ExplorerShell
{
    public static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static bool IsExplorer(string path) =>
        string.Equals(path, ExplorerPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while a shell owns the desktop window of this session.</summary>
    public static bool IsRunning() => GetShellWindow() != IntPtr.Zero;

    /// <summary>
    /// Starts Explorer as the shell of this session without administrator rights. An elevated app borrows the token
    /// of the session's shell infrastructure host (sihost.exe), which runs as the signed-in user without elevation,
    /// the same account Winlogon would start the shell under. Returns 0 or the Windows error code.
    /// </summary>
    public static int StartUnelevated()
    {
        using Process self = Process.GetCurrentProcess();
        if (!IsElevated(self.Handle))
        {
            try
            {
                using Process? started = Process.Start(new ProcessStartInfo(ExplorerPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExplorerPath) });
                return 0;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return ex.NativeErrorCode;
            }
        }

        int session = self.SessionId;
        int error = ErrorNotFound;
        foreach (Process host in Process.GetProcessesByName("sihost"))
        {
            using (host)
            {
                if (host.SessionId != session) continue;
                error = StartWithTokenOf(host.Id);
                if (error == 0) return 0;
            }
        }
        return error;
    }

    private static int StartWithTokenOf(int pid)
    {
        IntPtr process = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return Marshal.GetLastWin32Error();
        IntPtr token = IntPtr.Zero, primary = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, TokenDuplicate | TokenQuery, out token)) return Marshal.GetLastWin32Error();
            if (IsElevatedToken(token)) return ErrorElevationRequired;
            if (!DuplicateTokenEx(token, MaximumAllowed, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary)) return Marshal.GetLastWin32Error();

            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
            if (!CreateProcessWithTokenW(primary, 0, ExplorerPath, null, 0, IntPtr.Zero, Path.GetDirectoryName(ExplorerPath), ref startup, out ProcessInformation info))
                return Marshal.GetLastWin32Error();
            Win32.CloseHandle(info.Thread);
            Win32.CloseHandle(info.Process);
            return 0;
        }
        finally
        {
            if (primary != IntPtr.Zero) Win32.CloseHandle(primary);
            if (token != IntPtr.Zero) Win32.CloseHandle(token);
            Win32.CloseHandle(process);
        }
    }

    private static bool IsElevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TokenQuery, out IntPtr token)) return false;
        try
        {
            return IsElevatedToken(token);
        }
        finally
        {
            Win32.CloseHandle(token);
        }
    }

    private static bool IsElevatedToken(IntPtr token) =>
        GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0;

    private const int ErrorNotFound = 1168;
    private const int ErrorElevationRequired = 740;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr newToken);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string application, string? commandLine, int creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfo startup, out ProcessInformation info);
}
