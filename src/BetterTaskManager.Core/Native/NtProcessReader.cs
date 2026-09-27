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
    long OtherBytes);

/// <summary>
/// Reads every process in a single system call. This is the same source Task Manager uses for its
/// "Memory (private working set)" column, and it is far cheaper than opening each process.
/// </summary>
public static class NtProcessReader
{
    private const int SystemProcessInformation = 5;
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

    private static int s_bufferSize = 512 * 1024;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int bufferLength, out int returnLength);

    public static List<RawProcess> Read()
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Better Task Manager requires 64-bit Windows.");

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int size = s_bufferSize;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out int needed);
                if (status == StatusInfoLengthMismatch)
                {
                    // Processes can start between calls; leave headroom.
                    s_bufferSize = Math.Max(size * 2, needed + 64 * 1024);
                    continue;
                }
                if (status < 0) throw new Win32Exception($"NtQuerySystemInformation failed with NTSTATUS 0x{status:X8}.");
                return Parse(buffer, size);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("The process list changed too quickly to capture.");
    }

    private static List<RawProcess> Parse(IntPtr buffer, int size)
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
                OtherBytes: Marshal.ReadInt64(entry, OffsetOtherTransfer)));

            int next = Marshal.ReadInt32(entry, OffsetNextEntry);
            if (next == 0) break;
            offset += next;
        }
        return result;
    }
}
