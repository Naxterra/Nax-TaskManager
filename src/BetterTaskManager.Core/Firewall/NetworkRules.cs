using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterTaskManager.Core.Network;

namespace BetterTaskManager.Core.Firewall;

/// <summary>An address range (CIDR) of either IP version.</summary>
public readonly record struct Cidr(UInt128 Network, int Prefix, bool V6)
{
    public int Bits => V6 ? 128 : 32;

    public UInt128 First => Network;

    public UInt128 Last => Network | HostMask(Bits - Prefix);

    /// <summary>The lowest <paramref name="hostBits"/> bits set. Shifts by 128 wrap in C#, so the full width is a special case.</summary>
    private static UInt128 HostMask(int hostBits) => hostBits >= 128 ? UInt128.MaxValue : ((UInt128)1 << hostBits) - 1;

    public bool Contains(IPAddress address)
    {
        if ((address.AddressFamily == AddressFamily.InterNetworkV6) != V6) return false;
        UInt128 value = ToNumber(address);
        return value >= First && value <= Last;
    }

    public static UInt128 Max(bool v6) => v6 ? UInt128.MaxValue : uint.MaxValue;

    public static UInt128 ToNumber(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        UInt128 value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }

    public IPAddress Address
    {
        get
        {
            int length = V6 ? 16 : 4;
            byte[] bytes = new byte[length];
            UInt128 value = Network;
            for (int index = length - 1; index >= 0; index--)
            {
                bytes[index] = (byte)(value & 0xFF);
                value >>= 8;
            }
            return new IPAddress(bytes);
        }
    }

    public static bool TryParse(string text, out Cidr cidr)
    {
        cidr = default;
        string[] parts = text.Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out IPAddress? address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        bool v6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        int bits = v6 ? 128 : 32;
        int prefix = bits;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix > bits)) return false;
        cidr = new Cidr(ToNumber(address) & (Max(v6) ^ HostMask(bits - prefix)), prefix, v6);
        return true;
    }

    public override string ToString() => Prefix == Bits ? Address.ToString() : $"{Address}/{Prefix}";

    /// <summary>The smallest list of ranges covering [first, last].</summary>
    public static IEnumerable<Cidr> FromRange(UInt128 first, UInt128 last, bool v6)
    {
        int bits = v6 ? 128 : 32;
        if (first > last) yield break;
        while (true)
        {
            int size = 0;
            // Grow the block while it stays aligned and inside the range.
            while (size < bits)
            {
                UInt128 mask = HostMask(size + 1);
                if ((first & mask) != 0 || first + mask > last) break;
                size++;
            }
            yield return new Cidr(first, bits - size, v6);
            UInt128 end = first + HostMask(size);
            if (end >= last) yield break;
            first = end + 1;
        }
    }

    /// <summary>Everything in the address space of one IP version that none of <paramref name="excluded"/> covers.</summary>
    public static List<Cidr> Complement(IEnumerable<Cidr> excluded, bool v6)
    {
        var result = new List<Cidr>();
        UInt128 next = 0;
        bool done = false;
        foreach (Cidr range in excluded.Where(c => c.V6 == v6).OrderBy(c => c.First))
        {
            if (range.First > next) result.AddRange(FromRange(next, range.First - 1, v6));
            if (range.Last == Max(v6)) { done = true; break; }
            if (range.Last + 1 > next) next = range.Last + 1;
        }
        if (!done) result.AddRange(FromRange(next, Max(v6), v6));
        return result;
    }
}

/// <summary>
/// The address ranges behind the rule scopes. They follow <see cref="IpScopes.Classify"/> exactly, so what the app
/// shows as "LAN" or "Internet" is what a LAN or Internet rule enforces.
/// </summary>
public static class RuleScopes
{
    private static List<Cidr> Parse(params string[] ranges) => ranges.Select(text => Cidr.TryParse(text, out Cidr cidr) ? cidr : throw new FormatException(text)).ToList();

    public static IReadOnlyList<Cidr> Localhost { get; } = Parse("127.0.0.0/8", "::1/128");

    public static IReadOnlyList<Cidr> Lan { get; } = BuildLan();

    /// <summary>Addresses that are neither this PC, the LAN nor invalid (documentation, reserved).</summary>
    public static IReadOnlyList<Cidr> Internet { get; } = BuildInternet();

    private static List<Cidr> BuildLan()
    {
        var lan = Parse("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "169.254.0.0/16",
            "224.0.0.0/8", "239.0.0.0/8", "255.255.255.255/32", "fc00::/7", "fe80::/10");
        // IPv6 multicast with a scope of 5 (site) or less, whatever the flag bits: ffXY::/16 with Y <= 5.
        for (int flags = 0; flags < 16; flags++)
        {
            for (int scope = 0; scope <= 5; scope++) lan.AddRange(Parse($"ff{flags:x}{scope:x}::/16"));
        }
        return Merge(lan);
    }

    /// <summary>The same addresses in the fewest ranges (the complement of the complement), so filters stay small.</summary>
    private static List<Cidr> Merge(List<Cidr> ranges) =>
        Cidr.Complement(Cidr.Complement(ranges, v6: false), v6: false).Concat(Cidr.Complement(Cidr.Complement(ranges, v6: true), v6: true)).ToList();

    private static List<Cidr> BuildInternet()
    {
        var invalid = Parse("0.0.0.0/8", "240.0.0.0/4", "192.0.2.0/24", "198.51.100.0/24", "203.0.113.0/24", "::/128");
        var notInternet = Localhost.Concat(Lan).Concat(invalid).ToList();
        return Cidr.Complement(notInternet, v6: false).Concat(Cidr.Complement(notInternet, v6: true)).ToList();
    }
}

public enum RuleParseError
{
    None,
    Format,
    Action,
    Target,
    ProtocolPorts,
    Protocol,
    PortsNeedTcpOrUdp,
    Ports
}

public enum RuleTargetKind
{
    Any,
    Localhost,
    Lan,
    Internet,
    Address
}

/// <summary>
/// One line of an app's rule list, in the form <c>+|- target [protocol[/ports]] [# comment]</c>. The target is <c>*</c>,
/// an IP address, a CIDR range, or one of the scopes Localhost, LAN and Internet. The protocol is <c>*</c>, TCP, UDP,
/// ICMP or a number; ports are a number, a range like 1000-2000, or a well-known name such as HTTPS.
/// ICMP rules only reach programs with their own raw sockets: ping.exe and other users of the Windows ICMP API
/// have their echo requests sent by the system, so no per-app filter sees them (tested: not even a whole-app block).
/// </summary>
public sealed record NetworkRule(bool Allow, RuleTargetKind Target, Cidr? Address, int Protocol, int PortFrom, int PortTo, string Comment)
{
    /// <summary>Protocol "any"; with ports it means TCP or UDP, the protocols that have ports.</summary>
    public const int AnyProtocol = -1;
    public const int Icmp = 1, Tcp = 6, Udp = 17, IcmpV6 = 58;

    private static readonly Dictionary<string, int> PortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FTP"] = 21, ["SSH"] = 22, ["TELNET"] = 23, ["SMTP"] = 25, ["DNS"] = 53, ["HTTP"] = 80, ["POP3"] = 110, ["NTP"] = 123,
        ["IMAP"] = 143, ["SNMP"] = 161, ["LDAP"] = 389, ["HTTPS"] = 443, ["SMB"] = 445, ["SMTPS"] = 465, ["SUBMISSION"] = 587,
        ["IMAPS"] = 993, ["POP3S"] = 995, ["RDP"] = 3389
    };

    public bool HasPorts => PortFrom > 0;

    /// <summary>The ranges this rule's target covers for one IP version; empty when the target is of the other version.</summary>
    public IReadOnlyList<Cidr> Ranges(bool v6) => Target switch
    {
        RuleTargetKind.Any => [],
        RuleTargetKind.Localhost => RuleScopes.Localhost.Where(c => c.V6 == v6).ToList(),
        RuleTargetKind.Lan => RuleScopes.Lan.Where(c => c.V6 == v6).ToList(),
        RuleTargetKind.Internet => RuleScopes.Internet.Where(c => c.V6 == v6).ToList(),
        _ => Address is { } cidr && cidr.V6 == v6 ? [cidr] : []
    };

    /// <summary>False when the rule can never match this IP version (an address rule of the other version).</summary>
    public bool AppliesTo(bool v6) => Target != RuleTargetKind.Address || Address?.V6 == v6;

    /// <summary>The protocol numbers to match on one IP version; empty means any protocol.</summary>
    public IReadOnlyList<int> Protocols(bool v6) => Protocol switch
    {
        AnyProtocol => HasPorts ? [Tcp, Udp] : [],
        Icmp => [v6 ? IcmpV6 : Icmp],
        _ => [Protocol]
    };

    public bool Matches(IPAddress remote, int protocol, int port)
    {
        bool v6 = remote.AddressFamily == AddressFamily.InterNetworkV6;
        if (!AppliesTo(v6)) return false;
        if (Target != RuleTargetKind.Any && !Ranges(v6).Any(range => range.Contains(remote))) return false;
        IReadOnlyList<int> protocols = Protocols(v6);
        if (protocols.Count > 0 && !protocols.Contains(protocol)) return false;
        return !HasPorts || (port >= PortFrom && port <= PortTo);
    }

    public override string ToString()
    {
        string target = Target switch
        {
            RuleTargetKind.Any => "*",
            RuleTargetKind.Localhost => "Localhost",
            RuleTargetKind.Lan => "LAN",
            RuleTargetKind.Internet => "Internet",
            _ => Address?.ToString() ?? "?"
        };
        string text = (Allow ? "+ " : "- ") + target;
        if (Protocol != AnyProtocol || HasPorts)
        {
            string protocol = Protocol switch { AnyProtocol => "*", Tcp => "TCP", Udp => "UDP", Icmp => "ICMP", _ => Protocol.ToString(CultureInfo.InvariantCulture) };
            text += " " + protocol;
            if (HasPorts) text += "/" + (PortFrom == PortTo ? PortFrom.ToString(CultureInfo.InvariantCulture) : $"{PortFrom}-{PortTo}");
        }
        return Comment.Length > 0 ? $"{text} # {Comment}" : text;
    }

    /// <summary>Parses one rule line; <paramref name="error"/> says what is wrong in plain words.</summary>
    public static bool TryParse(string line, out NetworkRule? rule, out string error)
    {
        bool parsed = TryParse(line, out rule, out RuleParseError code, out string token);
        error = parsed ? "" : Describe(code, token);
        return parsed;
    }

    /// <summary>English text for a parse error; the app shows its own translation of the same code.</summary>
    public static string Describe(RuleParseError error, string token) => error switch
    {
        RuleParseError.Format => "Expected: + or -, then a target, then optionally protocol/port.",
        RuleParseError.Action => "A rule starts with + (allow) or - (block).",
        RuleParseError.Target => $"\"{token}\" is not *, LAN, Internet, Localhost, an IP address or a range.",
        RuleParseError.ProtocolPorts => $"\"{token}\" should look like TCP/443.",
        RuleParseError.Protocol => $"\"{token}\" is not a protocol (TCP, UDP, ICMP, * or a number).",
        RuleParseError.PortsNeedTcpOrUdp => "Only TCP and UDP have ports.",
        RuleParseError.Ports => $"\"{token}\" is not a port, a range like 1000-2000 or a name like HTTPS.",
        _ => ""
    };

    /// <summary>Parses one rule line; on failure <paramref name="token"/> is the part that is wrong.</summary>
    public static bool TryParse(string line, out NetworkRule? rule, out RuleParseError error, out string token)
    {
        rule = null;
        error = RuleParseError.None;
        token = "";
        string comment = "";
        int hash = line.IndexOf('#');
        if (hash >= 0)
        {
            comment = line[(hash + 1)..].Trim();
            line = line[..hash];
        }
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3) { error = RuleParseError.Format; return false; }
        bool allow;
        if (parts[0] == "+") allow = true;
        else if (parts[0] == "-") allow = false;
        else { error = RuleParseError.Action; token = parts[0]; return false; }

        RuleTargetKind kind;
        Cidr? address = null;
        switch (parts[1].ToUpperInvariant())
        {
            case "*": kind = RuleTargetKind.Any; break;
            case "LOCALHOST": kind = RuleTargetKind.Localhost; break;
            case "LAN": kind = RuleTargetKind.Lan; break;
            case "INTERNET": kind = RuleTargetKind.Internet; break;
            default:
                if (!Cidr.TryParse(parts[1], out Cidr cidr)) { error = RuleParseError.Target; token = parts[1]; return false; }
                kind = RuleTargetKind.Address;
                address = cidr;
                break;
        }

        int protocol = AnyProtocol, portFrom = 0, portTo = 0;
        if (parts.Length == 3)
        {
            string[] protocolPorts = parts[2].Split('/');
            if (protocolPorts.Length > 2) { error = RuleParseError.ProtocolPorts; token = parts[2]; return false; }
            string name = protocolPorts[0].ToUpperInvariant();
            protocol = name switch
            {
                "*" => AnyProtocol,
                "TCP" => Tcp,
                "UDP" => Udp,
                "ICMP" or "ICMP6" or "ICMPV6" => Icmp,
                _ => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number <= 255 ? number : -2
            };
            if (protocol == -2) { error = RuleParseError.Protocol; token = protocolPorts[0]; return false; }
            if (protocolPorts.Length == 2)
            {
                if (protocol is not (AnyProtocol or Tcp or Udp)) { error = RuleParseError.PortsNeedTcpOrUdp; token = parts[2]; return false; }
                if (!TryParsePorts(protocolPorts[1], out portFrom, out portTo)) { error = RuleParseError.Ports; token = protocolPorts[1]; return false; }
            }
        }
        rule = new NetworkRule(allow, kind, address, protocol, portFrom, portTo, comment);
        return true;
    }

    private static bool TryParsePorts(string text, out int from, out int to)
    {
        from = to = 0;
        string[] range = text.Split('-');
        if (range.Length > 2 || !TryPort(range[0], out from)) return false;
        to = from;
        if (range.Length == 2 && !TryPort(range[1], out to)) return false;
        return to >= from;

        static bool TryPort(string value, out int port)
        {
            if (PortNames.TryGetValue(value, out port)) return true;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
        }
    }

    /// <summary>Parses a whole rule list (one rule per line, empty lines ignored). Errors carry 1-based line numbers.</summary>
    public static List<NetworkRule> ParseList(string text, List<string> errors)
    {
        var rules = new List<NetworkRule>();
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]) || lines[index].TrimStart().StartsWith('#')) continue;
            if (TryParse(lines[index], out NetworkRule? rule, out string error)) rules.Add(rule!);
            else errors.Add($"{index + 1}: {error}");
        }
        return rules;
    }
}

/// <summary>Network settings of one program, like a Portmaster app profile without the DNS and SPN parts.</summary>
public sealed class AppNetworkPolicy
{
    /// <summary>Full path of the executable; rules apply to every process started from it.</summary>
    public string Path { get; set; } = "";
    public bool BlockInternet { get; set; }
    public bool BlockLan { get; set; }
    public bool BlockLocalhost { get; set; }
    /// <summary>Blocks connections other computers start to this program; connections from this PC stay allowed.</summary>
    public bool BlockInbound { get; set; }
    /// <summary>Rule lines for connections the program starts, first match wins.</summary>
    public List<string> Outgoing { get; set; } = [];
    /// <summary>Rule lines for connections other computers start to the program, first match wins.</summary>
    public List<string> Incoming { get; set; } = [];
    /// <summary>
    /// Helper programs that follow this app's rules and block: the browser engines some apps draw their window with
    /// (Steam's web helper, WebView2, CefSharp), which make the app's connections from their own program file.
    /// </summary>
    public List<string> Helpers { get; set; } = [];
    /// <summary>Set on a helper's own entry: the app whose rules it follows.</summary>
    public string? HelperOf { get; set; }

    /// <summary>True when switches or rules need filters; helpers alone do not.</summary>
    [JsonIgnore]
    public bool HasFilters => BlockInternet || BlockLan || BlockLocalhost || BlockInbound || Outgoing.Count > 0 || Incoming.Count > 0;

    [JsonIgnore]
    public bool IsEmpty => !HasFilters && Helpers.Count == 0;

    /// <summary>The same switches and rules for one of the app's helper programs.</summary>
    public AppNetworkPolicy ForHelper(string helper) => new()
    {
        Path = helper,
        BlockInternet = BlockInternet,
        BlockLan = BlockLan,
        BlockLocalhost = BlockLocalhost,
        BlockInbound = BlockInbound,
        Outgoing = [.. Outgoing],
        Incoming = [.. Incoming],
        HelperOf = Path
    };

    public List<NetworkRule> ParsedOutgoing() => Outgoing.Select(line => NetworkRule.TryParse(line, out NetworkRule? rule, out _) ? rule : null).OfType<NetworkRule>().ToList();

    public List<NetworkRule> ParsedIncoming() => Incoming.Select(line => NetworkRule.TryParse(line, out NetworkRule? rule, out _) ? rule : null).OfType<NetworkRule>().ToList();

    /// <summary>
    /// What the filters do with a connection: the per-app switches first (stronger than rules, as in Portmaster), then
    /// the rule list of that direction (first match), otherwise allowed. Returns null when allowed, else the reason
    /// code (see <see cref="PolicyReason"/>). For inbound, <paramref name="port"/> is the local port the connection
    /// arrives at.
    /// </summary>
    public string? Evaluate(bool inbound, IPAddress remote, int protocol, int port)
    {
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        bool localhost = RuleScopes.Localhost.Any(range => range.Contains(remote));
        if (inbound && BlockInbound && !localhost) return PolicyReason.BlockInbound;
        if (BlockLocalhost && localhost) return PolicyReason.BlockLocalhost;
        if (BlockLan && RuleScopes.Lan.Any(range => range.Contains(remote))) return PolicyReason.BlockLan;
        if (BlockInternet && RuleScopes.Internet.Any(range => range.Contains(remote))) return PolicyReason.BlockInternet;
        foreach (NetworkRule rule in inbound ? ParsedIncoming() : ParsedOutgoing())
        {
            if (rule.Matches(remote, protocol, port)) return rule.Allow ? null : PolicyReason.ForRule(rule);
        }
        return null;
    }
}

/// <summary>
/// Language-independent codes for why a connection is blocked. They are also the WFP filter names (after
/// <see cref="NamePrefix"/>), so a dropped packet's filter tells the reason without looking up the policy.
/// </summary>
public static class PolicyReason
{
    public const string NamePrefix = "Nax-TaskManager ";
    public const string WholeApp = "Block";
    public const string BlockInbound = "BlockInbound";
    public const string BlockLocalhost = "BlockLocalhost";
    public const string BlockLan = "BlockLan";
    public const string BlockInternet = "BlockInternet";
    public const string RulePrefix = "Rule:";

    public static string ForRule(NetworkRule rule) => RulePrefix + rule;
}

/// <summary>
/// All app policies, stored in the protected data folder (administrators write, everyone reads). The file describes
/// what the WFP filters enforce; the filters themselves are what blocks.
/// </summary>
public static class NetworkPolicyStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => System.IO.Path.Combine(DataFolder.Path, "app-rules.json");

    public static List<AppNetworkPolicy> Load()
    {
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return [];
    }

    /// <summary>Reads policies without turning a corrupt or unreadable configuration into an apparent allow.</summary>
    public static List<AppNetworkPolicy> Read(string? path = null)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AppNetworkPolicy>>(File.ReadAllText(path ?? FilePath), Options)
                ?? throw new JsonException("The policy file does not contain a policy list.");
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    public static AppNetworkPolicy? Find(string path) =>
        Load().FirstOrDefault(policy => string.Equals(policy.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces (or with an empty policy removes) one app's entry. Needs administrator rights.</summary>
    public static void Save(AppNetworkPolicy policy)
    {
        List<AppNetworkPolicy> all = Load();
        all.RemoveAll(existing => string.Equals(existing.Path, policy.Path, StringComparison.OrdinalIgnoreCase));
        if (!policy.IsEmpty) all.Add(policy);
        DataFolder.EnsureSecured();
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList(), Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public static void DeleteAll()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
