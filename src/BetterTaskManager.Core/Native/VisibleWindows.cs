using System.Text;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// One EnumWindows pass that finds the processes owning user-visible top-level windows. Task Manager uses
/// the same idea to split "Apps" from "Background processes". A single pass replaces the per-process
/// Process.MainWindowTitle lookups that dominated the old collector.
/// </summary>
public static class VisibleWindows
{
    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    public static Dictionary<int, string> ReadTitlesByProcess()
    {
        var titles = new Dictionary<int, string>();
        var text = new StringBuilder(256);
        Win32.EnumWindows((window, _) =>
        {
            if (!Win32.IsWindowVisible(window)) return true;
            if (Win32.GetWindow(window, GwOwner) != IntPtr.Zero) return true;
            if ((Win32.GetWindowLong(window, GwlExStyle) & WsExToolWindow) != 0) return true;
            if (Win32.DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            int length = Win32.GetWindowTextLength(window);
            if (length == 0) return true;

            Win32.GetWindowThreadProcessId(window, out int pid);
            if (pid == 0 || titles.ContainsKey(pid)) return true;

            text.Clear();
            text.EnsureCapacity(length + 1);
            Win32.GetWindowText(window, text, text.Capacity);
            titles[pid] = text.ToString();
            return true;
        }, IntPtr.Zero);
        return titles;
    }
}
