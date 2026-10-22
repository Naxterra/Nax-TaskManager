using System.Globalization;
using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Native;

/// <summary>
/// Keeps the standby list (Windows' cache of recently read and written file contents) below a limit the user chose:
/// the History service checks its size every 10 seconds and purges it when it is larger. Windows has no setting that
/// turns the standby list off; it refills it on every file access, so it can only be kept small. Purging costs
/// nothing at that moment, but files used again are then read from disk instead of RAM.
/// </summary>
public sealed class StandbyLimiter : IDisposable
{
    private const int SystemMemoryListInformation = 80;
    private const long PageSize = 4096;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(10);

    private readonly Action<string> log;
    private readonly CancellationTokenSource shutdown = new();
    private Thread? thread;
    private long? activeLimit;
    private int clears;
    private long bytesCleared;
    private DateTime summaryDue = DateTime.UtcNow + SummaryInterval;
    private bool reportedReadFailure;

    public StandbyLimiter(Action<string> log) => this.log = log;

    /// <summary>
    /// The limit in the protected data folder, as a number of megabytes; no file means off (the default). The service
    /// runs as SYSTEM and cannot read the user's settings, and only administrators may change files there.
    /// </summary>
    public static string ConfigPath => Path.Combine(DataFolder.Path, "standby-limit");

    public const int MinimumMegabytes = 256;

    /// <summary>The limit in bytes, or null when the standby list is left to Windows.</summary>
    public static long? LimitBytes
    {
        get
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                return long.TryParse(File.ReadAllText(ConfigPath).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long megabytes) && megabytes >= MinimumMegabytes
                    ? megabytes * 1048576
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Sets the limit in megabytes, or switches the limit off with null. Needs administrator rights.</summary>
    public static void SetLimit(int? megabytes)
    {
        if (megabytes is null)
        {
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            return;
        }
        DataFolder.EnsureSecured();
        File.WriteAllText(ConfigPath, Math.Max(MinimumMegabytes, megabytes.Value).ToString(CultureInfo.InvariantCulture));
    }

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "StandbyLimiter", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private void Run()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                Check();
            }
            catch (Exception ex)
            {
                log("Standby limit: " + ex.Message);
            }
            shutdown.Token.WaitHandle.WaitOne(CheckInterval);
        }
    }

    private void Check()
    {
        long? limit = LimitBytes;
        if (limit != activeLimit)
        {
            log(limit is { } on ? $"Standby limit: on, keeping the standby cache below {on / 1048576} MB" : "Standby limit: off");
            activeLimit = limit;
        }
        if (limit is null) return;

        if (ReadStandbyBytes() is not { } before)
        {
            if (!reportedReadFailure) log("Standby limit: the standby size cannot be read");
            reportedReadFailure = true;
            return;
        }
        if (before > limit)
        {
            CleanupResult result = MemoryCleanup.PurgeStandbyList();
            if (!result.Succeeded)
            {
                log("Standby limit: clearing failed: " + result.Message);
                return;
            }
            long after = ReadStandbyBytes() ?? 0;
            clears++;
            bytesCleared += Math.Max(0, before - after);
        }
        // One line per 10 minutes at most, so a busy disk cannot fill the log.
        if (DateTime.UtcNow >= summaryDue)
        {
            if (clears > 0) log($"Standby limit: cleared {clears} times in the last 10 minutes, {bytesCleared / 1048576} MB in total");
            clears = 0;
            bytesCleared = 0;
            summaryDue = DateTime.UtcNow + SummaryInterval;
        }
    }

    /// <summary>Standby pages of all priorities, from the kernel's memory list counters (needs SeProfileSingleProcessPrivilege).</summary>
    public static long? ReadStandbyBytes()
    {
        if (!MemoryCleanup.EnableProfilePrivilege()) return null;
        // SYSTEM_MEMORY_LIST_INFORMATION: 5 counters, PageCountByPriority[8], RepurposedPagesByPriority[8], 1 counter.
        var buffer = new IntPtr[22];
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            int status = NtQuerySystemInformation(SystemMemoryListInformation, pinned.AddrOfPinnedObject(), buffer.Length * IntPtr.Size, out _);
            if (status != 0) return null;
            long pages = 0;
            for (int priority = 0; priority < 8; priority++) pages += buffer[5 + priority].ToInt64();
            return pages * PageSize;
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        thread?.Join(TimeSpan.FromSeconds(5));
        shutdown.Dispose();
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);
}
