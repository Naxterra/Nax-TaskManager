using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Fluent.Services;

public static class ProcessActions
{
    public sealed record EndResult(int Ended, int Gone, List<string> Failures);

    public static bool AnyCritical(IEnumerable<(int Pid, long CreateTime)> processes) =>
        processes.Any(process => NativeProcessInfo.IsCritical(process.Pid));

    /// <summary>
    /// Ends processes, verifying each PID still belongs to the same process instance (creation time) so a PID
    /// reused by an unrelated process is never terminated.
    /// </summary>
    public static Task<EndResult> EndAsync(IReadOnlyList<(int Pid, long CreateTime)> processes, bool entireTree) => Task.Run(() =>
    {
        int ended = 0, gone = 0;
        var failures = new List<string>();
        foreach (var (pid, createTime) in processes)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.StartTime.ToFileTime() != createTime)
                {
                    gone++;
                    continue;
                }
                process.Kill(entireTree);
                ended++;
            }
            catch (ArgumentException)
            {
                gone++;
            }
            catch (InvalidOperationException)
            {
                gone++;
            }
            catch (Win32Exception ex)
            {
                failures.Add($"PID {pid}: {ex.Message}");
            }
        }
        return new EndResult(ended, gone, failures);
    });

    public sealed record ChangeResult(int Changed, List<string> Failures, bool AccessDenied);

    /// <summary>Applies a change to every process of a row; processes that exited in the meantime are skipped.</summary>
    public static Task<ChangeResult> ChangeAsync(IReadOnlyList<(int Pid, long CreateTime)> processes,
        Func<int, long, ControlResult> change) => Task.Run(() =>
    {
        int changed = 0;
        bool denied = false;
        var failures = new List<string>();
        foreach (var (pid, createTime) in processes)
        {
            ControlResult result = change(pid, createTime);
            if (result.Succeeded) changed++;
            else if (!result.Gone)
            {
                denied |= result.Error == 5;
                failures.Add($"PID {pid}: {new Win32Exception(result.Error).Message}");
            }
        }
        return new ChangeResult(changed, failures, denied);
    });

    public static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        // Only the path may be quoted: ArgumentList quotes the whole "/select,..." argument when the path has a space,
        // and Explorer then ignores it and opens Documents instead.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public static void ShowProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Verb = "properties",
            File = path,
            Show = 1,
            Mask = SeeMaskInvokeIdList
        };
        ShellExecuteEx(ref info);
    }

    public static void CopyText(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private const uint SeeMaskInvokeIdList = 0x0000000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string Verb;
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Directory;
        public int Show;
        public IntPtr InstanceApp;
        public IntPtr IdList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr IconOrMonitor;
        public IntPtr Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
}
