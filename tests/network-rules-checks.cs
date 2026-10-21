#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// Checks for the per-app network rules: parsing, address ranges, scopes and the first-match order.
// Run: dotnet run --file tests/network-rules-checks.cs   (no administrator rights needed; touches no filters)
using System.Net;
using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Network;

int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(ok || detail.Length == 0 ? "" : ": " + detail)}");
    if (!ok) failures++;
}

// 1. Parsing and printing round-trips, and bad lines are rejected with a reason.
string[] good = ["+ *", "- Internet", "+ LAN UDP/53", "- 10.0.0.0/8 TCP/1000-2000 # test", "+ ::1", "- 2001:db8::/32 * /443".Replace(" /", "/"), "+ 192.168.1.5 HTTPS".Replace(" HTTPS", " TCP/HTTPS"), "- * ICMP", "- * 47"];
foreach (string line in good)
{
    bool parsed = NetworkRule.TryParse(line, out NetworkRule? rule, out string error);
    Check($"parse \"{line}\"", parsed, error);
    if (parsed && NetworkRule.TryParse(rule!.ToString(), out NetworkRule? again, out _)) Check($"round-trip \"{line}\"", again == rule, rule.ToString());
}
string[] bad = ["* Internet", "+", "+ 300.1.1.1", "+ 10.0.0.0/33", "+ * ICMP/80", "+ * TCP/0", "+ * TCP/70000", "+ * TCP/20-10", "+ * FOO", "+ a b c d"];
foreach (string line in bad) Check($"reject \"{line}\"", !NetworkRule.TryParse(line, out _, out string why) && why.Length > 0);
Check("port names", NetworkRule.TryParse("+ * TCP/HTTPS", out NetworkRule? https, out _) && https!.PortFrom == 443);

// 2. Address ranges: complement and range splitting cover exactly what they should.
Check("cidr parse masks host bits", Cidr.TryParse("10.1.2.3/8", out Cidr ten) && ten.ToString() == "10.0.0.0/8", ten.ToString());
List<Cidr> notLoopback = Cidr.Complement(RuleScopes.Localhost, v6: false);
Check("v4 complement of 127/8 has no gaps or overlaps", notLoopback.Sum(c => (double)(c.Last - c.First + 1)) == Math.Pow(2, 32) - Math.Pow(2, 24), notLoopback.Count.ToString());
List<Cidr> v6NotLoopback = Cidr.Complement(RuleScopes.Localhost, v6: true);
Check("v6 complement of ::1 is 128 ranges", v6NotLoopback.Count == 128, v6NotLoopback.Count.ToString());
Console.WriteLine($"     Internet ranges: IPv4 {RuleScopes.Internet.Count(c => !c.V6)}, IPv6 {RuleScopes.Internet.Count(c => c.V6)}; LAN {RuleScopes.Lan.Count}");

// 3. The scopes agree with the labels the app shows (IpScopes), on fixed and random addresses.
string ScopeOf(IPAddress ip) =>
    RuleScopes.Localhost.Any(c => c.Contains(ip)) ? "HostLocal" :
    RuleScopes.Lan.Any(c => c.Contains(ip)) ? "Lan" :
    RuleScopes.Internet.Any(c => c.Contains(ip)) ? "Internet" : "Invalid";
string Expected(IPAddress ip) => IpScopes.Classify(ip) switch
{
    IpScope.HostLocal => "HostLocal",
    IpScope.LinkLocal or IpScope.SiteLocal or IpScope.LocalMulticast => "Lan",
    IpScope.Global or IpScope.GlobalMulticast => "Internet",
    _ => "Invalid"
};
var samples = new List<IPAddress>();
foreach (string text in new[] { "127.0.0.1", "10.0.0.1", "172.16.0.1", "172.32.0.1", "192.168.178.1", "100.64.0.1", "100.128.0.1", "169.254.1.1", "8.8.8.8",
    "224.0.0.251", "230.1.1.1", "239.255.255.250", "255.255.255.255", "0.0.0.0", "240.0.0.1", "192.0.2.1", "203.0.113.9",
    "::1", "::", "fe80::1", "fd00::1", "ff02::fb", "ff05::2", "ff0e::1", "ff15::1", "2a00:1450::1", "2001:db8::1", "64:ff9b::1" })
{
    samples.Add(IPAddress.Parse(text));
}
var random = new Random(42);
for (int index = 0; index < 20000; index++)
{
    byte[] bytes = new byte[index % 2 == 0 ? 4 : 16];
    random.NextBytes(bytes);
    if (bytes.Length == 16 && index % 3 == 0) bytes[0] = (byte)(0xFC + random.Next(4));
    if (bytes.Length == 16 && index % 5 == 0) bytes[0] = 0xFF;
    samples.Add(new IPAddress(bytes));
}
var mismatches = samples.Where(ip => ScopeOf(ip) != Expected(ip)).Take(5).Select(ip => $"{ip} rule={ScopeOf(ip)} label={Expected(ip)}").ToList();
Check("rule scopes match the shown scopes", mismatches.Count == 0, string.Join(", ", mismatches));

// 4. First match wins, the switches beat the rules, inbound rules compare the local port.
var policy = new AppNetworkPolicy
{
    Path = @"C:\Test\app.exe",
    BlockLan = true,
    Outgoing = ["+ 192.168.1.10 # never reached, the LAN switch is stronger", "+ 1.1.1.1 TCP/443", "- 1.1.1.0/24", "- Internet UDP"],
    Incoming = ["+ LAN TCP/8080", "- * TCP/8080"]
};
IPAddress Ip(string text) => IPAddress.Parse(text);
Check("switch beats rule", policy.Evaluate(false, Ip("192.168.1.10"), 6, 80) == PolicyReason.BlockLan);
Check("earlier allow wins", policy.Evaluate(false, Ip("1.1.1.1"), 6, 443) is null);
Check("later block applies", policy.Evaluate(false, Ip("1.1.1.1"), 6, 80)?.StartsWith(PolicyReason.RulePrefix + "- 1.1.1.0/24") == true);
Check("protocol matters", policy.Evaluate(false, Ip("8.8.8.8"), 17, 53)?.Contains("Internet UDP") == true && policy.Evaluate(false, Ip("8.8.8.8"), 6, 53) is null);
Check("default allows", policy.Evaluate(false, Ip("9.9.9.9"), 6, 443) is null);
Check("inbound local port", policy.Evaluate(true, Ip("8.8.8.8"), 6, 8080) is not null && policy.Evaluate(true, Ip("8.8.8.8"), 6, 9090) is null);
Check("inbound LAN switch beats inbound allow", policy.Evaluate(true, Ip("192.168.1.2"), 6, 8080) == PolicyReason.BlockLan);
var inboundOnly = new AppNetworkPolicy { Path = @"C:\Test\app.exe", BlockInbound = true };
Check("block inbound keeps localhost", inboundOnly.Evaluate(true, Ip("127.0.0.1"), 6, 80) is null && inboundOnly.Evaluate(true, Ip("10.0.0.2"), 6, 80) == PolicyReason.BlockInbound);
Check("block inbound leaves outbound", inboundOnly.Evaluate(false, Ip("10.0.0.2"), 6, 80) is null);

// 5. Compiling keeps the order and drops allows that have nothing to override.
List<PolicyFilter> filters = WfpBlocker.Compile(policy);
List<PolicyFilter> outV4 = filters.Where(f => !f.Inbound && !f.V6).ToList();
Check("outbound v4 order", outV4.Select(f => f.Reason).SequenceEqual([PolicyReason.BlockLan, "Rule:+ 192.168.1.10 # never reached, the LAN switch is stronger", "Rule:+ 1.1.1.1 TCP/443", "Rule:- 1.1.1.0/24", "Rule:- Internet UDP"]),
    string.Join(" | ", outV4.Select(f => f.Reason)));
List<PolicyFilter> outV6 = filters.Where(f => !f.Inbound && f.V6).ToList();
Check("outbound v6 skips v4 address rules", outV6.Select(f => f.Reason).SequenceEqual([PolicyReason.BlockLan, "Rule:- Internet UDP"]), string.Join(" | ", outV6.Select(f => f.Reason)));
var allowOnly = new AppNetworkPolicy { Path = @"C:\Test\app.exe", Outgoing = ["+ 1.1.1.1", "- 8.8.8.8", "+ *"] };
Check("trailing allows dropped", WfpBlocker.Compile(allowOnly).Count(f => !f.V6 && !f.Inbound) == 2 && WfpBlocker.Compile(allowOnly).All(f => !f.V6));
Check("ports only with TCP or UDP", WfpBlocker.Compile(new AppNetworkPolicy { Path = "x", Outgoing = ["- * */443"] })[0].Protocols.SequenceEqual([6, 17]));
Check("ICMP becomes ICMPv6 on v6", WfpBlocker.Compile(new AppNetworkPolicy { Path = "x", Outgoing = ["- * ICMP"] }).Single(f => f.V6).Protocols.SequenceEqual([58]));

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
