using System.Net;
using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Network;

/// <summary>Observed UDP peers supplement the socket table, which contains only bound local endpoints.</summary>
public static class UdpPeers
{
    internal static List<ConnectionSample> FromFlows(IReadOnlyList<ProcessSample> processes, IReadOnlyList<FlowSample> flows, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return [];
        var alive = processes.Select(process => process.Pid).ToHashSet();
        return FromRates(flows.Where(flow => flow.Protocol == "UDP" && alive.Contains(flow.Pid))
            .Select(flow => new SocketTraffic(flow.Pid, "UDP", flow.LocalPort, flow.RemoteAddress, flow.RemotePort,
                flow.Received / seconds, flow.Sent / seconds, flow.LocalAddress)));
    }

    internal static List<ConnectionSample> FromFeed(IReadOnlyList<ProcessSample> processes, NetworkFeedMessage message)
    {
        var identities = processes.ToDictionary(process => process.Pid, process => process.CreateTime);
        return FromRates((message.UdpPeers ?? []).Where(peer => peer.Protocol == "UDP" && peer.CreateTime != 0
            && identities.TryGetValue(peer.Pid, out long created) && created == peer.CreateTime));
    }

    private static List<ConnectionSample> FromRates(IEnumerable<SocketTraffic> rates) => rates
        .Where(peer => peer.LocalPort > 0 && peer.RemotePort > 0 && KnownAddress(peer.RemoteAddress)
            && peer.ReceivePerSecond + peer.SendPerSecond > 0)
        .GroupBy(peer => (peer.Pid, peer.LocalAddress, peer.LocalPort, peer.RemoteAddress, peer.RemotePort))
        .Select(group => new ConnectionSample
        {
            Pid = group.Key.Pid, Protocol = "UDP", LocalAddress = group.Key.LocalAddress,
            LocalPort = group.Key.LocalPort, RemoteAddress = group.Key.RemoteAddress, RemotePort = group.Key.RemotePort,
            State = "UDP", Scope = IpScopes.Classify(group.Key.RemoteAddress),
            ReceiveBytesPerSecond = group.Sum(peer => peer.ReceivePerSecond), SendBytesPerSecond = group.Sum(peer => peer.SendPerSecond)
        }).ToList();

    public static bool KnownAddress(string address) => IPAddress.TryParse(address, out var ip)
        && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any);

    public static IReadOnlyList<ConnectionSample> ForDisplay(IReadOnlyList<ConnectionSample> sockets, IReadOnlyList<ConnectionSample> peers)
    {
        if (peers.Count == 0) return sockets;
        var byPort = peers.GroupBy(peer => (peer.Pid, peer.LocalPort)).ToDictionary(group => group.Key, group => group.ToList());
        var socketTotals = sockets.Where(socket => socket.Protocol == "UDP").GroupBy(socket => (socket.Pid, socket.LocalPort))
            .ToDictionary(group => group.Key, group => (Down: group.Sum(socket => socket.ReceiveBytesPerSecond), Up: group.Sum(socket => socket.SendBytesPerSecond)));
        var replaced = new HashSet<(int, int)>();
        var shown = new HashSet<ConnectionSample>();
        var result = new List<ConnectionSample>();
        foreach (ConnectionSample socket in sockets)
        {
            var key = (socket.Pid, socket.LocalPort);
            if (socket.Protocol != "UDP" || !byPort.TryGetValue(key, out var matches)) { result.Add(socket); continue; }
            // The legacy socket rates are aggregated per process/port, sometimes on the IPv6 wildcard row.
            // Replace that aggregate once, rather than retaining it alongside the observed IPv4 peers.
            if (!replaced.Add(key)) continue;
            foreach (ConnectionSample peer in matches) if (shown.Add(peer)) result.Add(peer);
            var total = socketTotals[key];
            double down = Math.Max(0, total.Down - matches.Sum(peer => peer.ReceiveBytesPerSecond));
            double up = Math.Max(0, total.Up - matches.Sum(peer => peer.SendBytesPerSecond));
            if (down > 0.5 || up > 0.5) result.Add(new ConnectionSample
            {
                Pid = socket.Pid, Protocol = "UDP", LocalAddress = socket.LocalAddress, LocalPort = socket.LocalPort,
                RemoteAddress = "", RemotePort = 0, State = socket.State,
                ReceiveBytesPerSecond = down, SendBytesPerSecond = up
            });
        }
        // A datagram can be observed just before its socket closes. Its peer is still useful in this sample.
        foreach (ConnectionSample peer in peers) if (shown.Add(peer)) result.Add(peer);
        return result;
    }

}
