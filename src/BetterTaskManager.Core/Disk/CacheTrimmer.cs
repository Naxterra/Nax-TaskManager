using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BetterTaskManager.Core.Disk;

/// <summary>
/// Keeps big files out of RAM after they were written. Windows keeps every written byte in its file cache (the standby
/// list), so a 31 GB game update leaves 31 GB of game data in memory that nobody asked for. This watches the NTFS change
/// journal of each local drive; when a file of at least <see cref="Threshold"/> bytes was written and closed, it is
/// opened once without buffering 30 seconds later and again after 5 minutes (virus scanners often read a new file
/// right after it was written). Opening a file without buffering makes Windows drop its cached pages; files that a
/// running program still has open or mapped keep theirs. Needs administrator rights (volume handles); runs in the
/// history service.
/// </summary>
public sealed class CacheTrimmer : IDisposable
{
    public const long DefaultThreshold = 256L * 1024 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] Passes = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5)];

    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagNoBuffering = 0x20000000;
    private const uint FsctlQueryUsnJournal = 0x000900F4;
    private const uint FsctlReadUsnJournal = 0x000900BB;
    private const uint UsnReasonDataOverwrite = 0x1;
    private const uint UsnReasonDataExtend = 0x2;
    private const uint UsnReasonClose = 0x80000000;
    private const uint FileAttributeDirectory = 0x10;

    private sealed class Volume
    {
        public required string Letter { get; init; }
        public required SafeFileHandle Handle { get; init; }
        public ulong JournalId;
        public long NextUsn;
    }

    private sealed class Pending
    {
        public required Volume Volume { get; init; }
        public required long FileId { get; init; }
        public required string Path { get; init; }
        public required long Size { get; init; }
        public DateTime WrittenUtc;
        public int Pass;
    }

    private readonly Action<string> log;
    private readonly List<Volume> volumes = [];
    private readonly Dictionary<(string, long), Pending> pending = new();
    private readonly CancellationTokenSource shutdown = new();
    private Thread? thread;

    public CacheTrimmer(Action<string> log, long threshold = DefaultThreshold)
    {
        this.log = log;
        Threshold = threshold;
    }

    public long Threshold { get; }
    public long FilesTrimmed { get; private set; }
    public long BytesTrimmed { get; private set; }

    public void Start()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.DriveFormat != "NTFS") continue;
            }
            catch (IOException)
            {
                continue;
            }
            string letter = drive.Name.TrimEnd('\\');
            SafeFileHandle handle = CreateFile(@"\\.\" + letter, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                log($"Cache trim: cannot open {letter} ({new Win32Exception().Message})");
                continue;
            }
            var volume = new Volume { Letter = letter, Handle = handle };
            // Only drives that already keep a change journal; never create one.
            if (!QueryJournal(volume))
            {
                handle.Dispose();
                continue;
            }
            volumes.Add(volume);
        }
        log($"Cache trim: watching {string.Join(", ", volumes.Select(volume => volume.Letter))} for written files of {Threshold / 1048576} MB or more");
        if (volumes.Count == 0) return;
        thread = new Thread(Run) { IsBackground = true, Name = "CacheTrimmer", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private bool QueryJournal(Volume volume)
    {
        byte[] output = new byte[64];
        if (!DeviceIoControl(volume.Handle, FsctlQueryUsnJournal, null, 0, output, output.Length, out _, IntPtr.Zero)) return false;
        volume.JournalId = BitConverter.ToUInt64(output, 0);
        volume.NextUsn = BitConverter.ToInt64(output, 16);
        return true;
    }

    private void Run()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                foreach (Volume volume in volumes) ReadJournal(volume);
                TrimDue();
            }
            catch (Exception ex)
            {
                log("Cache trim failed: " + ex.Message);
            }
            if (shutdown.Token.WaitHandle.WaitOne(PollInterval)) return;
        }
    }

    private void ReadJournal(Volume volume)
    {
        byte[] input = new byte[40];
        byte[] output = new byte[64 * 1024];
        while (true)
        {
            // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask, ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID.
            BitConverter.TryWriteBytes(input.AsSpan(0), volume.NextUsn);
            BitConverter.TryWriteBytes(input.AsSpan(8), UsnReasonDataOverwrite | UsnReasonDataExtend | UsnReasonClose);
            BitConverter.TryWriteBytes(input.AsSpan(12), 1u);
            BitConverter.TryWriteBytes(input.AsSpan(16), 0UL);
            BitConverter.TryWriteBytes(input.AsSpan(24), 0UL);
            BitConverter.TryWriteBytes(input.AsSpan(32), volume.JournalId);
            if (!DeviceIoControl(volume.Handle, FsctlReadUsnJournal, input, input.Length, output, output.Length, out int returned, IntPtr.Zero))
            {
                // The journal wrapped (ERROR_JOURNAL_ENTRY_DELETED) or was recreated: continue from its current end.
                QueryJournal(volume);
                return;
            }
            if (returned <= 8) return;
            volume.NextUsn = BitConverter.ToInt64(output, 0);
            for (int offset = 8; offset + 60 <= returned;)
            {
                int length = BitConverter.ToInt32(output, offset);
                if (length <= 0) break;
                // USN_RECORD_V2 (what a V0 read returns): FileReferenceNumber at 8, Reason at 40, FileAttributes at 52.
                if (BitConverter.ToUInt16(output, offset + 4) == 2)
                {
                    long fileId = BitConverter.ToInt64(output, offset + 8);
                    uint reason = BitConverter.ToUInt32(output, offset + 40);
                    uint attributes = BitConverter.ToUInt32(output, offset + 52);
                    if ((reason & UsnReasonClose) != 0 && (reason & (UsnReasonDataOverwrite | UsnReasonDataExtend)) != 0 &&
                        (attributes & FileAttributeDirectory) == 0)
                    {
                        Consider(volume, fileId);
                    }
                }
                offset += length;
            }
        }
    }

    /// <summary>Schedules a written file for trimming when it is big enough; a new write restarts its schedule.</summary>
    private void Consider(Volume volume, long fileId)
    {
        using SafeFileHandle handle = OpenById(volume, fileId, FileReadAttributes, 0);
        if (handle.IsInvalid || !GetFileSizeEx(handle, out long size) || size < Threshold) return;
        string path = FinalPath(handle);
        // NTFS metadata files ($MFT, $LogFile...) are not ours to touch.
        if (path.Length == 0 || path.Contains(@"\$", StringComparison.Ordinal)) return;
        pending[(volume.Letter, fileId)] = new Pending { Volume = volume, FileId = fileId, Path = path, Size = size, WrittenUtc = DateTime.UtcNow };
    }

    private void TrimDue()
    {
        DateTime now = DateTime.UtcNow;
        foreach (var (key, item) in pending.ToList())
        {
            if (now - item.WrittenUtc < Passes[item.Pass]) continue;
            // Opening without buffering is what drops the cached pages; nothing is read or written.
            using (SafeFileHandle handle = OpenById(item.Volume, item.FileId, GenericRead, FileFlagNoBuffering))
            {
                if (!handle.IsInvalid && item.Pass == 0)
                {
                    FilesTrimmed++;
                    BytesTrimmed += item.Size;
                    log($"Cache trim: removed from RAM after write: {item.Path} ({item.Size / 1073741824d:0.00} GB)");
                }
            }
            item.Pass++;
            if (item.Pass >= Passes.Length) pending.Remove(key);
        }
    }

    private static SafeFileHandle OpenById(Volume volume, long fileId, uint access, uint flags)
    {
        // FILE_ID_DESCRIPTOR: dwSize, Type (FileIdType = 0), then the 64-bit file id in a 16-byte union.
        var descriptor = new FileIdDescriptor { Size = Marshal.SizeOf<FileIdDescriptor>(), Type = 0, FileId = fileId };
        return OpenFileById(volume.Handle, ref descriptor, access, ShareAll, IntPtr.Zero, flags);
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) return "";
        string path = buffer.ToString();
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    public void Dispose()
    {
        shutdown.Cancel();
        thread?.Join(TimeSpan.FromSeconds(5));
        foreach (Volume volume in volumes) volume.Handle.Dispose();
        volumes.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public int Size;
        public int Type;
        public long FileId;
        public long Padding;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(SafeFileHandle volume, ref FileIdDescriptor id, uint access, uint share, IntPtr security, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileSizeEx(SafeFileHandle file, out long size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
