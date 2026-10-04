using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Network;

/// <summary>
/// VPN tunnel processes send the encrypted copy of every other app's traffic, so counting them in "all apps"
/// totals counts the same bytes twice. Windows does not say which process owns a tunnel adapter; a tunnel here is a
/// session-0 (service) process named after a tunnel technology (or one of the few VPN daemons that carry the
/// tunnel themselves). VPN apps' windows and helper services (for example "Windscribe Service") are not tunnels.
/// </summary>
public static class VpnTunnels
{
    private static readonly string[] Keywords =
    [
        "wireguard", "openvpn", "wintun", "wiresock", "amneziawg", "tailscale", "zerotier", "warp-svc", "nordlynx",
        "softether", "vpnclient", "netbird", "mullvad-daemon", "expressvpnd", "fortisslvpn", "pangps", "vpnagent"
    ];

    public static bool IsTunnel(ProcessSample process) =>
        process.SessionId == 0 && process.Pid > 4 &&
        (Matches(process.ImageName) || Matches(process.Path) || Matches(process.Description) ||
         (process.Services?.Any(Matches) ?? false));

    public static bool Matches(string? value) =>
        !string.IsNullOrEmpty(value) && Keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
