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
    public const string SessionName = "BetterTaskManager-Network";

    private readonly object gate = new();
    private Dictionary<int, ByteCounts> byProcess = new();
    private Dictionary<SocketKey, ByteCounts> bySocket = new();
    private TraceEventSession? session;
    private Thread? pump;

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "Not started";

    public bool TryStart()
    {
        if (IsRunning) return true;
        if (TraceEventSession.IsElevated() != true)
        {
            Status = "Per-app network speed needs administrator rights.";
            return false;
        }

        try
        {
            // Reusing the fixed name replaces a session left behind by a crashed instance.
            session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            KernelTraceEventParser kernel = session.Source.Kernel;
            kernel.TcpIpSend += data => Add(data.ProcessID, true, false, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpRecv += data => Add(data.ProcessID, true, true, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpSendIPV6 += data => Add(data.ProcessID, true, false, data.size, data.sport, data.daddr, data.dport);
            kernel.TcpIpRecvIPV6 += data => Add(data.ProcessID, true, true, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpSend += data => Add(data.ProcessID, false, false, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpRecv += data => Add(data.ProcessID, false, true, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpSendIPV6 += data => Add(data.ProcessID, false, false, data.size, data.sport, data.daddr, data.dport);
            kernel.UdpIpRecvIPV6 += data => Add(data.ProcessID, false, true, data.size, data.sport, data.daddr, data.dport);

            pump = new Thread(() =>
            {
                try
                {
                    session.Source.Process();
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
            ControlTrace(0, SessionName, properties, EventTraceControlFlush);
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
        IsRunning = false;
        session?.Dispose();
        session = null;
        pump?.Join(TimeSpan.FromSeconds(2));
    }
}
