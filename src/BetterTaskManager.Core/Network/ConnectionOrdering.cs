using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Network;

public static class ConnectionOrdering
{
    public static IEnumerable<ConnectionSample> Sort(IEnumerable<ConnectionSample> connections, string column, bool descending)
    {
        static int Rank(ConnectionSample c) => c.State == "Established" ? 0 : c.State == "Listening" ? 2 : 1;
        static string Host(ConnectionSample c) => c.RemoteHost ?? c.RemoteAddress;
        IOrderedEnumerable<ConnectionSample> ordered = column switch
        {
            "Speed" => descending ? connections.OrderByDescending(c => c.ReceiveBytesPerSecond + c.SendBytesPerSecond)
                : connections.OrderBy(c => c.ReceiveBytesPerSecond + c.SendBytesPerSecond),
            "Remote" => descending ? connections.OrderByDescending(Host, StringComparer.OrdinalIgnoreCase)
                : connections.OrderBy(Host, StringComparer.OrdinalIgnoreCase),
            "State" => descending ? connections.OrderByDescending(c => c.State, StringComparer.OrdinalIgnoreCase)
                : connections.OrderBy(c => c.State, StringComparer.OrdinalIgnoreCase),
            _ => connections.OrderBy(Rank)
        };
        return ordered.ThenBy(Rank).ThenBy(Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemotePort).ThenBy(c => c.LocalAddress, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.LocalPort).ThenBy(c => c.Pid);
    }
}
