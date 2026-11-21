using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace BetterTaskManager.Core.Disk;

/// <summary>Disk traffic of one process on one file over the last minute, as in Resource Monitor's "Disk Activity".</summary>
public sealed record FileActivity(int Pid, string File, double ReadPerSecond, double WritePerSecond, double ResponseMilliseconds, string Priority, int DiskNumber);

/// <summary>
/// Per-file disk activity from the kernel's DiskIO events (the source Resource Monitor uses). Needs administrator
/// rights. Meant to run only while someone looks at it: the Disk page starts it and stops it when it is left, so
/// the trace's kernel buffers exist only then.
/// </summary>
public sealed class DiskFileActivity : IDisposable
{
    public const string DefaultSessionName = "NaxTaskManager-Disk";
    private const int WindowSeconds = 60;
    private const int KernelBufferSizeMB = 8;
    private static readonly TimeSpan RundownInterval = TimeSpan.FromSeconds(30);

    private sealed class Entry
    {
        public readonly long[] Read = new long[WindowSeconds];
        public readonly long[] Write = new long[WindowSeconds];
        public readonly double[] ResponseSum = new double[WindowSeconds];
        public readonly int[] Count = new int[WindowSeconds];
        public long LastSecond;
        public string File = "";
        public string Priority = "";
        public int DiskNumber;
    }

    private readonly object gate = new();
    private readonly Dictionary<(int Pid, ulong FileKey), Entry> entries = new();
    /// <summary>File names by kernel file key, from name events and from rundowns of the files already open.</summary>
    private readonly ConcurrentDictionary<ulong, string> names = new();
    /// <summary>Image names from the trace's process events, so short-lived processes can still be named.</summary>
    private readonly ConcurrentDictionary<int, string> processNames = new();
    /// <summary>When each traced process exited (tick count), so names of long-gone processes can be dropped.</summary>
    private readonly ConcurrentDictionary<int, long> processExited = new();
    private const int MaxFileNames = 200_000, MaxProcessNames = 4096;
    private readonly string sessionName;
    private readonly object rundownGate = new();
    private TraceEventSession? session;
    private Thread? pump;
    private CancellationTokenSource? rundownCancel;
    private Task? rundownTask;
    private long startedSecond;
    private long lastRundown;
    private int rundownRunning;
    private int nameCount;
    private volatile bool running;

    public DiskFileActivity(string sessionName = DefaultSessionName) => this.sessionName = sessionName;

    public bool IsRunning => running;
    public string Status { get; private set; } = "";

    public bool Start()
    {
        if (running) return true;
        if (TraceEventSession.IsElevated() != true)
        {
            Status = "Needs administrator rights.";
            return false;
        }
        try
        {
            session?.Dispose();
            session = new TraceEventSession(sessionName) { StopOnDispose = true, BufferSizeMB = KernelBufferSizeMB };
            // DiskFileIO names files opened from now on; Thread lets the parser attribute completions to processes;
            // Process names processes that exit before the app's next snapshot.
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO | KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.Process);
            KernelTraceEventParser kernel = session.Source.Kernel;
            kernel.DiskIORead += data => Add(data, read: true);
            kernel.DiskIOWrite += data => Add(data, read: false);
            kernel.FileIOName += data => Remember(data.FileKey, data.FileName);
            kernel.FileIOFileCreate += data => Remember(data.FileKey, data.FileName);
            kernel.ProcessStart += data => RememberProcess(data.ProcessID, data.ImageFileName);
            kernel.ProcessDCStart += data => RememberProcess(data.ProcessID, data.ImageFileName);
            kernel.ProcessStop += data => processExited[data.ProcessID] = Environment.TickCount64;
            TraceEventSession started = session;
            startedSecond = Environment.TickCount64 / 1000;
            lock (rundownGate)
            {
                rundownCancel?.Dispose();
                rundownCancel = new CancellationTokenSource();
            }
            running = true;
            Status = "";
            pump = new Thread(() =>
            {
                try { started.Source.Process(); }
                catch (Exception ex) { Status = ex.Message; }
                running = false;
            })
            { IsBackground = true, Name = "DiskFileActivity" };
            pump.Start();
            RequestRundown();
            return true;
        }
        catch (Exception ex)
        {
            running = false;
            Status = ex.Message;
            session?.Dispose();
            session = null;
            return false;
        }
    }

    public void Stop()
    {
        running = false;
        Task? pendingRundown;
        lock (rundownGate)
        {
            rundownCancel?.Cancel();
            pendingRundown = rundownTask;
        }
        if (pendingRundown is not null)
        {
            try { pendingRundown.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        }
        lock (rundownGate)
        {
            rundownCancel?.Dispose();
            rundownCancel = null;
            rundownTask = null;
        }
        session?.Dispose();
        session = null;
        pump?.Join(TimeSpan.FromSeconds(2));
        pump = null;
        lock (gate) entries.Clear();
        names.Clear();
        Interlocked.Exchange(ref nameCount, 0);
        processNames.Clear();
        processExited.Clear();
    }

    /// <summary>Image name of a process the trace saw start or running; empty when unknown.</summary>
    public string ProcessName(int pid) => processNames.TryGetValue(pid, out string? name) ? name : "";

    private void Remember(ulong fileKey, string? name)
    {
        if (fileKey == 0 || string.IsNullOrEmpty(name)) return;
        // File keys are never reported as freed here; start over when there are very many, and the next rundown names
        // the files that are still open.
        if (Volatile.Read(ref nameCount) >= MaxFileNames)
        {
            names.Clear();
            Interlocked.Exchange(ref nameCount, 0);
        }
        if (names.TryAdd(fileKey, name)) Interlocked.Increment(ref nameCount);
        else names[fileKey] = name;
    }

    private void RememberProcess(int pid, string name)
    {
        processExited.TryRemove(pid, out _);
        processNames[pid] = name;
        if (processNames.Count <= MaxProcessNames) return;
        // Keep a minute after exit, long enough for the next snapshot to name a short-lived process.
        long now = Environment.TickCount64;
        foreach (var (gone, at) in processExited)
        {
            if (now - at < 60_000) continue;
            processNames.TryRemove(gone, out _);
            processExited.TryRemove(gone, out _);
        }
    }

    /// <summary>
    /// Names the files that were already open when the trace started. Windows lists every open file when a session
    /// with DiskFileIO stops, so a separate session is started and stopped right away and its list is kept.
    /// </summary>
    private void RequestRundown()
    {
        CancellationToken token;
        lock (rundownGate)
        {
            if (rundownCancel is null || rundownCancel.IsCancellationRequested) return;
            if (Interlocked.Exchange(ref rundownRunning, 1) == 1) return;
            token = rundownCancel.Token;
            lastRundown = Environment.TickCount64;
            rundownTask = Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    using var rundown = new TraceEventSession(sessionName + "-Names") { StopOnDispose = true, BufferSizeMB = 16 };
                    rundown.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskFileIO);
                    rundown.Source.Kernel.FileIOFileRundown += data => Remember(data.FileKey, data.FileName);
                    rundown.Source.Kernel.FileIOName += data => Remember(data.FileKey, data.FileName);
                    var reader = new Thread(() =>
                    {
                        try { rundown.Source.Process(); } catch (Exception) { }
                    })
                    { IsBackground = true, Name = "DiskFileNames" };
                    reader.Start();
                    token.WaitHandle.WaitOne(500);
                    rundown.Stop();
                    reader.Join(TimeSpan.FromSeconds(token.IsCancellationRequested ? 2 : 10));
                }
                catch (Exception)
                {
                    // Names stay unknown for files opened before the trace; the traffic is still counted.
                }
                finally
                {
                    Interlocked.Exchange(ref rundownRunning, 0);
                }
            });
        }
    }

    internal static string FormatPriority(IOPriority priority) => priority switch
    {
        IOPriority.Verylow => "VeryLow",
        IOPriority.Low => "Low",
        IOPriority.Normal => "Normal",
        IOPriority.High => "High",
        IOPriority.Critical => "Critical",
        _ => ""
    };

    private void Add(DiskIOTraceData data, bool read)
    {
        if (data.TransferSize <= 0 || data.ProcessID < 0) return;
        long second = Environment.TickCount64 / 1000;
        int slot = (int)(second % WindowSeconds);
        string priorityName = FormatPriority(data.Priority);
        lock (gate)
        {
            var key = (data.ProcessID, data.FileKey);
            if (!entries.TryGetValue(key, out Entry? entry)) entries[key] = entry = new Entry { LastSecond = second };
            Advance(entry, second);
            if (read) entry.Read[slot] += data.TransferSize;
            else entry.Write[slot] += data.TransferSize;
            entry.ResponseSum[slot] += data.ElapsedTimeMSec;
            entry.Count[slot]++;
            if (data.FileName is { Length: > 0 } name) entry.File = name;
            entry.Priority = priorityName;
            entry.DiskNumber = data.DiskNumber;
        }
    }

    /// <summary>Clears the seconds that passed since the entry was last touched, so old traffic leaves the window.</summary>
    private static void Advance(Entry entry, long second)
    {
        long gap = Math.Min(second - entry.LastSecond, WindowSeconds);
        for (long passed = 1; passed <= gap; passed++)
        {
            int slot = (int)((entry.LastSecond + passed) % WindowSeconds);
            entry.Read[slot] = entry.Write[slot] = 0;
            entry.ResponseSum[slot] = 0;
            entry.Count[slot] = 0;
        }
        entry.LastSecond = Math.Max(entry.LastSecond, second);
    }

    /// <summary>Every process and file with disk traffic in the last minute, averaged per second over that minute.</summary>
    public List<FileActivity> Read()
    {
        long second = Environment.TickCount64 / 1000;
        double divisor = startedSecond > 0 ? Math.Clamp(second - startedSecond + 1, 1, WindowSeconds) : WindowSeconds;
        var result = new List<FileActivity>();
        bool unnamed = false;
        lock (gate)
        {
            foreach (var (key, entry) in entries.ToList())
            {
                Advance(entry, second);
                int count = entry.Count.Sum();
                if (count == 0)
                {
                    entries.Remove(key);
                    continue;
                }
                if (names.TryGetValue(key.FileKey, out string? known)) entry.File = known;
                unnamed |= entry.File.Length == 0 && key.FileKey != 0;
                result.Add(new FileActivity(key.Pid, entry.File, entry.Read.Sum() / divisor, entry.Write.Sum() / divisor,
                    entry.ResponseSum.Sum() / count, entry.Priority, entry.DiskNumber));
            }
        }
        if (unnamed && IsRunning && Environment.TickCount64 - lastRundown > RundownInterval.TotalMilliseconds) RequestRundown();
        return result;
    }

    public void Dispose() => Stop();
}
