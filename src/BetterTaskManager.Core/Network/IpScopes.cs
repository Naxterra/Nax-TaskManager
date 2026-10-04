using System.Net;
using System.Net.Sockets;

namespace BetterTaskManager.Core.Network;

/// <summary>Where an address lives, following Portmaster's IP scopes.</summary>
public enum IpScope
{
    Invalid,
    HostLocal,
    LinkLocal,
    SiteLocal,
    Global,
    LocalMulticast,
    GlobalMulticast
}

public static class IpScopes
{
    public static IpScope Classify(string address) =>
        IPAddress.TryParse(address, out IPAddress? ip) ? Classify(ip) : IpScope.Invalid;

    public static IpScope Classify(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 127) return IpScope.HostLocal;
            if (b[0] == 169 && b[1] == 254) return IpScope.LinkLocal;
            if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ||
                (b[0] == 100 && b[1] >= 64 && b[1] <= 127)) // RFC 1918 and carrier-grade NAT (RFC 6598)
            {
                return IpScope.SiteLocal;
            }
            if (b[0] == 0 || b[0] >= 240) return b is [255, 255, 255, 255] ? IpScope.LocalMulticast : IpScope.Invalid;
            if (b[0] == 224 || b[0] == 239) return IpScope.LocalMulticast;
            if (b[0] >= 225 && b[0] <= 238) return IpScope.GlobalMulticast;
            if ((b[0] == 192 && b[1] == 0 && b[2] == 2) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113))
            {
                return IpScope.Invalid; // documentation ranges
            }
            return IpScope.Global;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback)) return IpScope.HostLocal;
            if (ip.Equals(IPAddress.IPv6Any)) return IpScope.Invalid;
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return IpScope.LinkLocal;
            if ((b[0] & 0xFE) == 0xFC) return IpScope.SiteLocal; // unique local fc00::/7
            if (b[0] == 0xFF) return (b[1] & 0x0F) <= 5 ? IpScope.LocalMulticast : IpScope.GlobalMulticast;
            return IpScope.Global;
        }
        return IpScope.Invalid;
    }

    public static string Label(IpScope scope) => scope switch
    {
        IpScope.HostLocal => "This PC",
        IpScope.LinkLocal => "Link-local",
        IpScope.SiteLocal => "LAN",
        IpScope.Global => "Internet",
        IpScope.LocalMulticast => "Multicast",
        IpScope.GlobalMulticast => "Multicast",
        _ => ""
    };
}
