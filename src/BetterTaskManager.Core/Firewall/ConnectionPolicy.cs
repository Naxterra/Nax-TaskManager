using System.Net;
using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Core.Firewall;

public enum PolicyOutcome { Unknown, NotApplicable, Allowed, Blocked }
public sealed record PolicyExplanation(PolicyOutcome Outcome, string Reason = "", string Rule = "");

/// <summary>Explains our configured rules. This is not a captured WFP packet verdict.</summary>
public static class ConnectionPolicy
{
    public static PolicyExplanation Explain(ConnectionSample connection, bool outboundBlocked, AppNetworkPolicy? policy)
    {
        if (connection.Protocol == "TCP" && connection.State == "Listening") return new(PolicyOutcome.NotApplicable);
        // A UDP table row only describes a local binding; it supplies neither a peer nor its direction.
        if (connection.Protocol != "TCP" || !IPAddress.TryParse(connection.RemoteAddress, out IPAddress? remote) ||
            remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any)) return new(PolicyOutcome.Unknown);
        if (!connection.Inbound && outboundBlocked) return new(PolicyOutcome.Blocked, PolicyReason.WholeApp);
        if (policy is null) return new(PolicyOutcome.Allowed);
        int port = connection.Inbound ? connection.LocalPort : connection.RemotePort;
        string? reason = policy.Evaluate(connection.Inbound, remote, 6, port);
        if (reason is not null) return new(PolicyOutcome.Blocked, reason);
        NetworkRule? matched = (connection.Inbound ? policy.ParsedIncoming() : policy.ParsedOutgoing())
            .FirstOrDefault(rule => rule.Matches(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote, 6, port));
        return new(PolicyOutcome.Allowed, Rule: matched?.ToString() ?? "");
    }
}
