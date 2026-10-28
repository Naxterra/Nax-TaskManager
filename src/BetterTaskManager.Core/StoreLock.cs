using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace BetterTaskManager.Core;

/// <summary>
/// Machine-wide locks around changes that read, change and write a stored file together with the system state it
/// describes. Two elevated helpers (two UAC prompts), an elevated window and the History service can otherwise change
/// the same file at the same time, and one change would silently undo the other. The locks are Windows mutexes that
/// SYSTEM and Administrators may use whoever created them, and the same thread may take one again while it holds it.
/// </summary>
public static class StoreLock
{
    /// <summary>Network rules, whole-app blocks and their filters (app-rules.json, blocked-apps.txt, WFP).</summary>
    public const string Rules = @"Global\NaxTaskManager.Rules";

    /// <summary>The memory manager switches and their records (memory-manager.json, memory-manager-original.json).</summary>
    public const string MemoryManager = @"Global\NaxTaskManager.MemoryManager";

    private const uint MutexAllAccess = 0x1F0001, WaitAbandoned = 0x80, WaitObject0 = 0;
    private const string SystemAndAdministrators = "D:(A;;GA;;;SY)(A;;GA;;;BA)";
    private static readonly TimeSpan MaximumWait = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Waits for the lock and holds it until the result is disposed. A process that may not open the lock (not
    /// elevated) goes ahead without it, because only elevated processes change what it protects. So does a change
    /// that waited a minute: no change takes that long, so the holder is stuck or is not this app.
    /// </summary>
    public static IDisposable Take(string name) => Take(name, SystemAndAdministrators);

    /// <summary>The same with another access list for a lock this call creates (tests run without elevation).</summary>
    public static IDisposable Take(string name, string accessSddl)
    {
        var descriptor = new RawSecurityDescriptor(accessSddl);
        byte[] binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        GCHandle pinned = GCHandle.Alloc(binary, GCHandleType.Pinned);
        SafeWaitHandle handle;
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pinned.AddrOfPinnedObject() };
            handle = CreateMutexEx(ref attributes, name, 0, MutexAllAccess);
        }
        finally
        {
            pinned.Free();
        }
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return new Held(null);
        }
        uint wait = WaitForSingleObject(handle, (uint)MaximumWait.TotalMilliseconds);
        // WAIT_ABANDONED: a holder ended without releasing it. This thread owns it now, and the files are written
        // atomically, so there is nothing half-done to repair.
        if (wait != WaitObject0 && wait != WaitAbandoned)
        {
            handle.Dispose();
            return new Held(null);
        }
        return new Held(handle);
    }

    private sealed class Held(SafeWaitHandle? handle) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released || handle is null) return;
            released = true;
            ReleaseMutex(handle);
            handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReleaseMutex(SafeWaitHandle handle);
}
