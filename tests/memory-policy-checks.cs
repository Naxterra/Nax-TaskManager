#:property PublishAot=false
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../src/BetterTaskManager.Core/BetterTaskManager.Core.csproj
// Pure fixtures: no memory trimming, firewall changes or service installation.
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Firewall;

int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures++;
}
Dictionary<string, long> Counters() => new()
{
    ["Modified Page List Bytes"] = 100,
    ["Standby Cache Core Bytes"] = 50, ["Standby Cache Normal Priority Bytes"] = 100,
    ["Standby Cache Reserve Bytes"] = 50, ["Free & Zero Page List Bytes"] = 100,
    ["Pool Paged Resident Bytes"] = 40, ["Pool Nonpaged Bytes"] = 60,
    ["System Cache Resident Bytes"] = 50, ["System Driver Resident Bytes"] = 50
};
var full = MemoryAccounting.Create(1000, 300, 1500, 2000, 300, Counters());
Check("physical categories reconcile", full.InUse == 600 && full.InUse + full.Modified + full.Available == full.Total);
Check("standby is already available", full.AvailableDifference == 0 && full.Standby == 200);
Check("private RAM differs from virtual commit", full.ProcessPrivate == 300 && full.CommitTotal == 1500);
Check("resident pools and residual", full.KernelPools == 100 && full.Unattributed == 100 && full.Detailed);
var missing = Counters(); missing.Remove("Pool Paged Resident Bytes");
Check("partial counters are not complete", !MemoryAccounting.Create(1000, 300, 0, 0, 300, missing).Detailed);
missing = Counters(); missing.Remove("Free & Zero Page List Bytes");
Check("partial page lists flagged independently", !MemoryAccounting.Create(1000, 300, 0, 0, 300, missing).PageListsAvailable);
var absent = MemoryAccounting.Create(1000, 300, 0, 0, 300, new Dictionary<string, long>());
Check("fallback preserves physical RAM", absent.InUse == 700 && !absent.ModifiedAvailable && !absent.Detailed);
var excess = MemoryAccounting.Create(1000, 300, 0, 0, 900, Counters());
Check("negative residual is disclosed", excess.AttributionDifference == -500 && excess.Unattributed == 0);
var drift = MemoryAccounting.Create(1000, 290, 0, 0, 300, Counters());
Check("page-list sample difference retained", drift.AvailableDifference == -10);
var bounded = MemoryAccounting.Create(100, 99, 0, 0, 0, Counters());
Check("physical composition never goes negative", bounded.InUse >= 0 && bounded.InUse + bounded.Modified + bounded.Available == 100);
missing = Counters(); missing["Pool Nonpaged Bytes"] = -1;
Check("invalid negative counter is unavailable", !MemoryAccounting.Create(1000, 300, 0, 0, 0, missing).Detailed);

ConnectionSample Socket(string remote = "8.8.8.8", int remotePort = 443, int localPort = 50000,
    bool inbound = false, string protocol = "TCP", string state = "Established") => new()
{
    Pid = 123, Protocol = protocol, LocalAddress = "192.168.1.2", LocalPort = localPort,
    RemoteAddress = remote, RemotePort = remotePort, Inbound = inbound, State = state
};
var rules = new AppNetworkPolicy { Outgoing = ["+ * TCP/443", "- *"], Incoming = ["- * TCP/8080"] };
var allowed = ConnectionPolicy.Explain(Socket(), false, rules);
Check("first allow rule is explained", allowed.Outcome == PolicyOutcome.Allowed && allowed.Rule.Contains("443"));
Check("first matching deny is explained", ConnectionPolicy.Explain(Socket(remotePort: 80), false, rules).Reason.StartsWith(PolicyReason.RulePrefix));
Check("inbound rules use local destination port", ConnectionPolicy.Explain(Socket(localPort: 8080, inbound: true), false, rules).Outcome == PolicyOutcome.Blocked);
Check("outbound app block takes precedence", ConnectionPolicy.Explain(Socket(), true, rules).Reason == PolicyReason.WholeApp);
Check("outbound app block does not claim inbound block", ConnectionPolicy.Explain(Socket(inbound: true), true, null).Outcome == PolicyOutcome.Allowed);
Check("listener is not a blocked connection", ConnectionPolicy.Explain(Socket(state: "Listening"), true, rules).Outcome == PolicyOutcome.NotApplicable);
Check("UDP local binding is not a policy verdict", ConnectionPolicy.Explain(Socket(remote: "0.0.0.0", protocol: "UDP"), true, rules).Outcome == PolicyOutcome.Unknown);
Check("unknown peer is not allowed by default", ConnectionPolicy.Explain(Socket(remote: ""), false, rules).Outcome == PolicyOutcome.Unknown);
Check("IPv4-mapped rule scope matches", ConnectionPolicy.Explain(Socket(remote: "::ffff:8.8.8.8"), false, new AppNetworkPolicy { BlockInternet = true }).Reason == PolicyReason.BlockInternet);
Check("default allow is limited to our configuration", ConnectionPolicy.Explain(Socket(), false, null).Outcome == PolicyOutcome.Allowed);
string temporary = Path.Combine(Path.GetTempPath(), "nax-policy-read-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    Check("missing policy file is an empty configuration", NetworkPolicyStore.Read(temporary).Count == 0);
    File.WriteAllText(temporary, "[{\"Path\":\"C:\\\\Test.exe\",\"BlockInternet\":true}]");
    Check("valid policy configuration is read", NetworkPolicyStore.Read(temporary) is [{ BlockInternet: true }]);
    File.WriteAllText(temporary, "{broken");
    bool failed = false;
    try { NetworkPolicyStore.Read(temporary); } catch (System.Text.Json.JsonException) { failed = true; }
    Check("corrupt policy is not an apparent allow", failed);
    File.WriteAllText(temporary, "null");
    failed = false;
    try { NetworkPolicyStore.Read(temporary); } catch (System.Text.Json.JsonException) { failed = true; }
    Check("null policy file is not an apparent allow", failed);
}
finally { File.Delete(temporary); }
return failures == 0 ? 0 : 1;
