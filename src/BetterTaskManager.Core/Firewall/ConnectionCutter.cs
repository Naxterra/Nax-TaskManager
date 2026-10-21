using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Core.Network;

namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// Makes a new block take effect on connections that are already open. WFP checks open connections against
/// changed filters again and drops their packets, but a TCP connection whose packets are dropped only fails after
/// its retries run out. IPv4 TCP connections are therefore closed at once (a reset, as Resource Monitor's "End
/// connection" does); Windows has no such call for IPv6, so those rely on the filters alone.
/// </summary>
public static class ConnectionCutter
{
    private const uint StateDeleteTcb = 12;

    public readonly record struct Result(int Closed, int Failed);

    /// <summary>
    /// Closes the open IPv4 TCP connections of every process started from <paramref name="path"/> for which
    /// <paramref name="blocked"/> returns true. Its arguments are: inbound, remote address, and the port the rules
    /// compare (the local port for inbound, the remote port for outbound). Needs administrator rights.
    /// </summary>
    public static Result Cut(string path, Func<bool, IPAddress, int, bool> blocked)
    {
        NativeNetworkSnapshot snapshot = NativeNetworkCollector.GetSnapshot();
        var pathOfPid = new Dictionary<int, bool>();
        bool IsApp(int pid)
        {
            if (!pathOfPid.TryGetValue(pid, out bool matches))
            {
                matches = pid > 4 && string.Equals(Win32.QueryImagePath(pid), path, StringComparison.OrdinalIgnoreCase);
                pathOfPid[pid] = matches;
            }
            return matches;
        }

        var listening = snapshot.Connections
            .Where(row => row.Protocol == "TCP" && row.State == "Listening")
            .Select(row => (row.OwningPid, row.LocalPort))
            .ToHashSet();
        int closed = 0, failed = 0;
        foreach (NativeConnection row in snapshot.Connections)
        {
            if (row.Protocol != "TCP" || row.State is not ("Established" or "Syn Sent" or "Syn Received" or "Close Wait")) continue;
            if (!IPAddress.TryParse(row.RemoteAddress, out IPAddress? remote) || !IPAddress.TryParse(row.LocalAddress, out IPAddress? local)) continue;
            if (!IsApp(row.OwningPid)) continue;
            bool inbound = listening.Contains((row.OwningPid, row.LocalPort));
            if (!blocked(inbound, remote, inbound ? row.LocalPort : row.RemotePort)) continue;
            if (remote.AddressFamily != AddressFamily.InterNetwork) continue;
            var entry = new TcpRow
            {
                State = StateDeleteTcb,
                LocalAddress = BitConverter.ToUInt32(local.GetAddressBytes()),
                LocalPort = NetworkPort(row.LocalPort),
                RemoteAddress = BitConverter.ToUInt32(remote.GetAddressBytes()),
                RemotePort = NetworkPort(row.RemotePort)
            };
            if (SetTcpEntry(ref entry) == 0) closed++;
            else failed++;
        }
        return new Result(closed, failed);
    }

    /// <summary>MIB_TCPROW keeps the port in network byte order in the low 16 bits.</summary>
    private static uint NetworkPort(int port) => (uint)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetTcpEntry(ref TcpRow row);
}
