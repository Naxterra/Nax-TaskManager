using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace BetterTaskManager.Core.Network;

/// <summary>Bytes moved by one socket, keyed from the owning process's point of view.</summary>
public readonly record struct SocketKey(int Pid, bool Tcp, int LocalPort, IPAddress RemoteAddress, int RemotePort);

public struct ByteCounts
{
    public long Received;
    public long Sent;
}

/// <summary>
/// Per-process and per-connection network throughput from the kernel's TCP/IP ETW events — the same source
/// Resource Monitor uses. Needs administrator rights to open the trace session. Loopback traffic is ignored
/// because it never leaves the machine.
/// </summary>
public sealed class BandwidthMonitor : IDisposable
{
    /// <summary>A process seen by the kernel trace; kept after exit so short-lived processes can still be named.</summary>
    public readonly record struct TracedProcess(string Name, string Path, DateTime? ExitedUtc);
    private static readonly TimeSpan ExitedProcessMemory = TimeSpan.FromMinutes(10);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TracedProcess> processes = new();

    public const string DefaultSessionName = "NaxTaskManager-Network";
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private readonly string sessionName;
    private Dictionary<int, ByteCounts> byProcess = new();
    private Dictionary<SocketKey, ByteCounts> bySocket = new();
    private TraceEventSession? session;
    private Thread? pump;
    private volatile bool disposed;
    private DateTime lastAttempt;

    /// <param name="sessionName">
    /// ETW session names are machine-wide and opening one replaces any session with the same name, so a second
    /// program using the same name silently stops this one. Tests must pass their own name.
    /// </param>
    public BandwidthMonitor(string sessionName = DefaultSessionName) => this.sessionName = sessionName;

    private const int KernelBufferSizeMB = 16;

    public bool IsRunning { get; private set; }

    /// <summary>Events the kernel dropped because the buffers were full (0 when the buffer size is adequate).</summary>
    public long EventsLost => session?.EventsLost ?? 0;
    public string Status { get; private set; } = "Not started";

    public bool TryStart()
    {
        if (IsRunning) return true;
        lastAttempt = DateTime.UtcNow;
        if (TraceEventSession.IsElevated() != true)
        {
            Status = "Per-app network speed needs administrator rights.";
            return false;
        }

        try
        {
            // Reusing the fixed name replaces a session left behind by a crashed instance.
            session?.Dispose();
            // The library default reserves 64 MB of non-pageable kernel memory per session. Buffers are flushed every
            // second, so 16 MB holds several seconds of TCP/IP events even at full gigabit speed.
            session = new TraceEventSession(sessionName) { StopOnDispose = true, BufferSizeMB = KernelBufferSizeMB };
            // Process events name connections of processes that exit before the next sample (CLI tools, updaters).
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);

            KernelTraceEventParser kernel = session.Source.Kernel;
            kernel.TcpIpSend += data => Add(data.ProcessID, true, false, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpRecv += data => Add(data.ProcessID, true, true, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpSendIPV6 += data => Add(data.ProcessID, true, false, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpRecvIPV6 += data => Add(data.ProcessID, true, true, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpSend += data => Add(data.ProcessID, false, false, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpRecv += data => Add(data.ProcessID, false, true, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpSendIPV6 += data => Add(data.ProcessID, false, false, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpRecvIPV6 += data => Add(data.ProcessID, false, true, data.size, data.sport, data.daddr, data.dport);
            kernel.ProcessStart += OnProcessStart;
            kernel.ProcessDCStart += OnProcessStart;
            kernel.ProcessStop += data =>
            {
                if (processes.TryGetValue(data.ProcessID, out TracedProcess known)) processes[data.ProcessID] = known with { ExitedUtc = DateTime.UtcNow };
            };

            TraceEventSession started = session;
            pump = new Thread(() =>
            {
                try
                {
                    started.Source.Process();
                    Status = "The network trace was stopped by another program; restarting.";
                }
                catch (Exception ex)
                {
                    Status = "Network trace stopped: " + ex.Message;
                }
                IsRunning = false;
            })
            { IsBackground = true, Name = "BandwidthMonitor" };
            pump.Start();

            IsRunning = true;
            Status = "Running";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Could not start the network trace: " + ex.Message;
            session?.Dispose();
            session = null;
            return false;
        }
    }

    /// <summary>Restarts the trace after it was stopped from outside, at most once per <see cref="RestartDelay"/>.</summary>
    public void EnsureRunning()
    {
        if (IsRunning || disposed || DateTime.UtcNow - lastAttempt < RestartDelay) return;
        TryStart();
    }

    private void OnProcessStart(ProcessTraceData data)
    {
        if (data.ProcessID <= 4) return;
        processes[data.ProcessID] = new TracedProcess(data.ImageFileName, BetterTaskManager.Core.Native.DevicePaths.ToDosPath(data.KernelImageFileName), null);
    }

    /// <summary>Name and path of a process the trace saw start, including ones that exited in the last minutes.</summary>
    public bool TryGetProcess(int pid, out TracedProcess process)
    {
        if (!processes.TryGetValue(pid, out process)) return false;
        if (process.ExitedUtc is { } exited && DateTime.UtcNow - exited > ExitedProcessMemory)
        {
            processes.TryRemove(pid, out _);
            return false;
        }
        return true;
    }

    private void Add(int pid, bool tcp, bool received, int size, int localPort, IPAddress remote, int remotePort)
    {
        if (size <= 0 || pid <= 0 || IPAddress.IsLoopback(remote)) return;
        var key = new SocketKey(pid, tcp, localPort, remote, remotePort);
        lock (gate)
        {
            ref ByteCounts process = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(byProcess, pid, out _);
            ref ByteCounts socket = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(bySocket, key, out _);
            if (received)
            {
                process.Received += size;
                socket.Received += size;
            }
            else
            {
                process.Sent += size;
                socket.Sent += size;
            }
        }
    }

    /// <summary>
    /// Asks Windows to deliver buffered trace events now. The kernel otherwise hands them over about once a
    /// second, which would shift traffic between refresh intervals and make rates jump between 0 and double.
    /// </summary>
    public void Flush()
    {
        if (!IsRunning) return;
        const int PropertiesSize = 120;
        const int NameBytes = 1024 * 2;
        IntPtr properties = System.Runtime.InteropServices.Marshal.AllocHGlobal(PropertiesSize + 2 * NameBytes);
        try
        {
            unsafe
            {
                new Span<byte>((void*)properties, PropertiesSize + 2 * NameBytes).Clear();
            }
            System.Runtime.InteropServices.Marshal.WriteInt32(properties, 0, PropertiesSize + 2 * NameBytes); // Wnode.BufferSize
            System.Runtime.InteropServices.Marshal.WriteInt32(properties, 112, PropertiesSize + NameBytes);   // LogFileNameOffset
            System.Runtime.InteropServices.Marshal.WriteInt32(properties, 116, PropertiesSize);               // LoggerNameOffset
            ControlTrace(0, sessionName, properties, EventTraceControlFlush);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(properties);
        }
    }

    private const uint EventTraceControlFlush = 3;

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "ControlTraceW")]
    private static extern uint ControlTrace(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

    /// <summary>Returns the bytes counted since the previous call and starts a new interval.</summary>
    public (Dictionary<int, ByteCounts> Processes, Dictionary<SocketKey, ByteCounts> Sockets) Drain()
    {
        lock (gate)
        {
            var result = (byProcess, bySocket);
            byProcess = new Dictionary<int, ByteCounts>();
            bySocket = new Dictionary<SocketKey, ByteCounts>();
            return result;
        }
    }

    public void Dispose()
    {
        disposed = true;
        IsRunning = false;
        session?.Dispose();
        session = null;
        pump?.Join(TimeSpan.FromSeconds(2));
    }
}
