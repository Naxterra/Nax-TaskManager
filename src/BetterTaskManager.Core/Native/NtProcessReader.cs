using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>Raw per-process counters from one NtQuerySystemInformation(SystemProcessInformation) call.</summary>
public readonly record struct RawProcess(
    int Pid,
    int ParentPid,
    string Name,
    long CreateTime,
    long CpuTime100ns,
    long PrivateWorkingSet,
    long WorkingSet,
    long PeakWorkingSet,
    long CommitCharge,
    int ThreadCount,
    int HandleCount,
    int SessionId,
    long ReadBytes,
    long WriteBytes,
    long OtherBytes,
    bool Suspended,
    long DiskReadBytes = 0,
    long DiskWriteBytes = 0);

/// <summary>
/// Reads every process in a single system call. This is the same source Task Manager uses for its
/// "Memory (private working set)" column, and it is far cheaper than opening each process. With administrator
/// rights (or as LocalSystem) the "full" variant also returns each process's disk counters: bytes that actually
/// reached a disk, which is what Task Manager's Disk column and Resource Monitor show.
/// </summary>
public static class NtProcessReader
{
    private const int SystemProcessInformation = 5;
    private const int SystemFullProcessInformation = 148;
    private const int StatusAccessDenied = unchecked((int)0xC0000022);
    private const int StatusInvalidInfoClass = unchecked((int)0xC0000003);
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    // SYSTEM_PROCESS_INFORMATION field offsets for 64-bit Windows.
    private const int OffsetNextEntry = 0;
    private const int OffsetThreadCount = 4;
    private const int OffsetWorkingSetPrivate = 8;
    private const int OffsetCreateTime = 32;
    private const int OffsetUserTime = 40;
    private const int OffsetKernelTime = 48;
    private const int OffsetImageNameLength = 56;
    private const int OffsetImageNameBuffer = 64;
    private const int OffsetPid = 80;
    private const int OffsetParentPid = 88;
    private const int OffsetHandleCount = 96;
    private const int OffsetSessionId = 100;
    private const int OffsetPeakWorkingSet = 136;
    private const int OffsetWorkingSet = 144;
    private const int OffsetPrivatePageCount = 200;
    private const int OffsetReadTransfer = 232;
    private const int OffsetWriteTransfer = 240;
    private const int OffsetOtherTransfer = 248;

    // The SYSTEM_THREAD_INFORMATION array follows the 256-byte process entry.
    private const int ProcessEntrySize = 256;
    private const int ThreadEntrySize = 80;
    // The full variant returns SYSTEM_EXTENDED_THREAD_INFORMATION entries (thread info plus stack and TEB fields),
    // followed by SYSTEM_PROCESS_INFORMATION_EXTENSION, which starts with the disk counters.
    private const int ExtendedThreadEntrySize = 136;
    private const int OffsetDiskBytesRead = 0;
    private const int OffsetDiskBytesWritten = 8;
    private const int OffsetThreadState = 68;
    private const int OffsetWaitReason = 72;
    private const int ThreadStateWaiting = 5;
    private const int WaitReasonSuspended = 5;

    private static int s_bufferSize = 512 * 1024;
    private static volatile bool s_fullDenied;

    /// <summary>True once a read returned per-process disk counters (needs administrator rights).</summary>
    public static bool DiskCountersAvailable { get; private set; }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int bufferLength, out int returnLength);

    public static List<RawProcess> Read()
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Nax-TaskManager requires 64-bit Windows.");

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int size = s_bufferSize;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                bool full = !s_fullDenied;
                int status = NtQuerySystemInformation(full ? SystemFullProcessInformation : SystemProcessInformation, buffer, size, out int needed);
                if (full && status is StatusAccessDenied or StatusInvalidInfoClass)
                {
                    // Without administrator rights Windows refuses the full variant; keep using the basic one.
                    s_fullDenied = true;
                    DiskCountersAvailable = false;
                    full = false;
                    status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out needed);
                }
                if (status == StatusInfoLengthMismatch)
                {
                    // Processes can start between calls; leave headroom.
                    s_bufferSize = Math.Max(size * 2, needed + 64 * 1024);
                    continue;
                }
                if (status < 0) throw new Win32Exception($"NtQuerySystemInformation failed with NTSTATUS 0x{status:X8}.");
                DiskCountersAvailable = full;
                return Parse(buffer, size, full);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("The process list changed too quickly to capture.");
    }

    private static List<RawProcess> Parse(IntPtr buffer, int size, bool full)
    {
        var result = new List<RawProcess>(512);
        int offset = 0;
        while (true)
        {
            if (offset < 0 || offset + OffsetOtherTransfer + 8 > size) break;
            IntPtr entry = buffer + offset;

            int nameBytes = Marshal.ReadInt16(entry, OffsetImageNameLength) & 0xFFFF;
            IntPtr namePointer = Marshal.ReadIntPtr(entry, OffsetImageNameBuffer);
            int pid = (int)Marshal.ReadInt64(entry, OffsetPid);
            string name = namePointer == IntPtr.Zero || nameBytes == 0
                ? (pid == 0 ? "System Idle Process" : "")
                : Marshal.PtrToStringUni(namePointer, nameBytes / 2);
            // The full variant names processes by their NT path (\Device\HarddiskVolume3\...\app.exe); keep the file name.
            if (full) name = Path.GetFileName(name);

            result.Add(new RawProcess(
                Pid: pid,
                ParentPid: (int)Marshal.ReadInt64(entry, OffsetParentPid),
                Name: name,
                CreateTime: Marshal.ReadInt64(entry, OffsetCreateTime),
                CpuTime100ns: Marshal.ReadInt64(entry, OffsetUserTime) + Marshal.ReadInt64(entry, OffsetKernelTime),
                PrivateWorkingSet: Marshal.ReadInt64(entry, OffsetWorkingSetPrivate),
                WorkingSet: Marshal.ReadInt64(entry, OffsetWorkingSet),
                PeakWorkingSet: Marshal.ReadInt64(entry, OffsetPeakWorkingSet),
                CommitCharge: Marshal.ReadInt64(entry, OffsetPrivatePageCount),
                ThreadCount: Marshal.ReadInt32(entry, OffsetThreadCount),
                HandleCount: Marshal.ReadInt32(entry, OffsetHandleCount),
                SessionId: Marshal.ReadInt32(entry, OffsetSessionId),
                ReadBytes: Marshal.ReadInt64(entry, OffsetReadTransfer),
                WriteBytes: Marshal.ReadInt64(entry, OffsetWriteTransfer),
                OtherBytes: Marshal.ReadInt64(entry, OffsetOtherTransfer),
                Suspended: pid > 4 && AllThreadsSuspended(entry, offset, size, full),
                DiskReadBytes: full ? ReadExtension(entry, offset, size, OffsetDiskBytesRead) : 0,
                DiskWriteBytes: full ? ReadExtension(entry, offset, size, OffsetDiskBytesWritten) : 0));

            int next = Marshal.ReadInt32(entry, OffsetNextEntry);
            if (next == 0) break;
            offset += next;
        }
        return result;
    }

    /// <summary>Task Manager's "Suspended": every thread waits with the Suspended reason (typical for parked Store apps).</summary>
    private static bool AllThreadsSuspended(IntPtr entry, int offset, int size, bool full)
    {
        int threads = Marshal.ReadInt32(entry, OffsetThreadCount);
        int threadSize = full ? ExtendedThreadEntrySize : ThreadEntrySize;
        if (threads <= 0 || offset + ProcessEntrySize + (long)threads * threadSize > size) return false;
        for (int index = 0; index < threads; index++)
        {
            int thread = ProcessEntrySize + index * threadSize;
            if (Marshal.ReadInt32(entry, thread + OffsetThreadState) != ThreadStateWaiting ||
                Marshal.ReadInt32(entry, thread + OffsetWaitReason) != WaitReasonSuspended)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A field of the extension block that follows the extended thread entries of a full-variant entry.</summary>
    private static long ReadExtension(IntPtr entry, int offset, int size, int field)
    {
        long position = ProcessEntrySize + (long)Marshal.ReadInt32(entry, OffsetThreadCount) * ExtendedThreadEntrySize + field;
        return offset + position + 8 <= size ? Marshal.ReadInt64(entry, (int)position) : 0;
    }
}
