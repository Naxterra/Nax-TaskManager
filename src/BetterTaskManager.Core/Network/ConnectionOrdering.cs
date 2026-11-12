using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Network;

public static class ConnectionOrdering
{
    public static IEnumerable<ConnectionSample> Sort(IEnumerable<ConnectionSample> connections, string column, bool descending,
        Func<ConnectionSample, string>? appName = null, Func<ConnectionSample, string>? scopeLabel = null)
    {
        static int Rank(ConnectionSample c) => c.State == "Established" ? 0 : c.State == "Listening" ? 2 : 1;
        static string Host(ConnectionSample c) => c.RemoteHost ?? c.RemoteAddress;
        appName ??= c => c.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        scopeLabel ??= c => c.Scope.ToString();
        IOrderedEnumerable<ConnectionSample> ordered = column switch
        {
            "Download" => descending ? connections.OrderByDescending(c => c.ReceiveBytesPerSecond)
                : connections.OrderBy(c => c.ReceiveBytesPerSecond),
            "Upload" => descending ? connections.OrderByDescending(c => c.SendBytesPerSecond)
                : connections.OrderBy(c => c.SendBytesPerSecond),
            "Speed" => descending ? connections.OrderByDescending(c => c.ReceiveBytesPerSecond + c.SendBytesPerSecond)
                : connections.OrderBy(c => c.ReceiveBytesPerSecond + c.SendBytesPerSecond),
            "Remote" => descending ? connections.OrderByDescending(Host, StringComparer.OrdinalIgnoreCase)
                : connections.OrderBy(Host, StringComparer.OrdinalIgnoreCase),
            "State" => descending ? connections.OrderByDescending(c => c.State, StringComparer.OrdinalIgnoreCase)
                : connections.OrderBy(c => c.State, StringComparer.OrdinalIgnoreCase),
            "App" => descending ? connections.OrderByDescending(appName, StringComparer.CurrentCultureIgnoreCase)
                : connections.OrderBy(appName, StringComparer.CurrentCultureIgnoreCase),
            "Local" => descending ? connections.OrderByDescending(c => c.LocalAddress, StringComparer.OrdinalIgnoreCase).ThenByDescending(c => c.LocalPort)
                : connections.OrderBy(c => c.LocalAddress, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.LocalPort),
            "Scope" => descending ? connections.OrderByDescending(scopeLabel, StringComparer.CurrentCultureIgnoreCase)
                : connections.OrderBy(scopeLabel, StringComparer.CurrentCultureIgnoreCase),
            _ => connections.OrderBy(Rank)
        };
        return ordered.ThenBy(Rank).ThenBy(Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.RemotePort).ThenBy(c => c.LocalAddress, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.LocalPort)
            .ThenBy(c => c.Pid).ThenBy(c => c.Protocol, StringComparer.Ordinal);
    }
}
