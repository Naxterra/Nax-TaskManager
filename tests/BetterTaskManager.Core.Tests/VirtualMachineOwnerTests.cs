using System.Security.Principal;
using System.Text.Json;
using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Tests;

internal static class VirtualMachineOwnerTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("vm_owners_match_runtime_id_and_process_identity", "VM memory processes get the owner of their own VM; ambiguous, missing and non-VM identities get no label.", () =>
        {
            Guid coworkId = Guid.Parse("12345678-1234-4abc-8123-000000000001");
            Guid otherId = Guid.Parse("12345678-1234-4abc-8123-000000000002");
            Guid ambiguousId = Guid.Parse("12345678-1234-4abc-8123-000000000003");
            var sid = new SecurityIdentifier("S-1-5-83-1-305419896-1253839412-9089-16777216");
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            Check.That(VirtualMachineOwners.TryReadVmId(bytes, out Guid decoded), "VM SID was rejected");
            Check.Equal(coworkId, decoded);
            bytes[12] = 0;
            Check.That(!VirtualMachineOwners.TryReadVmId(bytes, out _), "non-VM SID was accepted");

            var owners = VirtualMachineOwners.ParseComputeOwners($$"""
                [{"RuntimeId":"{{coworkId}}","SystemType":"VirtualMachine","Id":"cowork-vm-example","Owner":"cowork-vm-example"},
                 {"RuntimeId":"{{otherId}}","SystemType":"VirtualMachine","Owner":"WSL"},
                 {"RuntimeId":"{{ambiguousId}}","SystemType":"VirtualMachine","Owner":"first"},
                 {"RuntimeId":"{{ambiguousId}}","SystemType":"VirtualMachine","Owner":"second"}]
                """);
            RawProcess Process(int pid, long created, string name) => default(RawProcess) with { Pid = pid, CreateTime = created, Name = name };
            var matches = VirtualMachineOwners.Match([Process(20, 10, "vmmem"), Process(21, 10, "VmmemWSL"), Process(22, 10, "ordinary.exe"),
                Process(23, 10, "vmmem"), Process(24, 10, "vmmem")], new Dictionary<int, Guid>
                { [20] = decoded, [21] = otherId, [22] = decoded, [23] = ambiguousId }, owners);
            Check.Equal(2, matches.Count);
            Check.Equal("Cowork VM", matches[(20, 10)]);
            Check.Equal("WSL", matches[(21, 10)]);
            Check.That(!matches.ContainsKey((20, 11)), "an owner was assigned to a reused PID");
        });

        yield return ("vm_owner_feed_uses_latest_frame_and_supports_older_services", "VM labels survive feed serialization and are discarded when absent from the newest sample.", () =>
        {
            var frame = new NetworkFeedMessage(DateTime.UtcNow, [], [], VmOwners: [new(20, 10, "Cowork VM")]);
            var parsed = JsonSerializer.Deserialize(JsonSerializer.Serialize(frame, NetworkFeedJson.Default.NetworkFeedMessage), NetworkFeedJson.Default.NetworkFeedMessage)!;
            Check.Equal(frame.VmOwners!.Single(), parsed.VmOwners!.Single());
            using var client = new NetworkFeedClient();
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            client.Accept(parsed, now);
            Check.Equal("Cowork VM", client.Current!.VmOwners!.Single().Name);
            client.Accept(frame with { VmOwners = null }, now);
            Check.That(client.Current!.VmOwners is null, "a stale owner survived a frame with no ownership data");
        });
    }
}
