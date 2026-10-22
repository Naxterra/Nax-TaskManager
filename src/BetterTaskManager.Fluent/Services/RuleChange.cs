using BetterTaskManager.Core.Firewall;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Everything the Rules page saves for one app in one step: the whole-app network block, the network rules and the
/// memory rule. A part that is null stays as it is. Applied directly when elevated, otherwise by one elevated helper.
/// </summary>
public sealed class RuleChange
{
    public string Path { get; set; } = "";
    public bool? Blocked { get; set; }
    public AppNetworkPolicy? Network { get; set; }
    public AppMemoryRule? Memory { get; set; }

    public const int BlockFailed = 1, NetworkFailed = 2, MemoryFailed = 4, Invalid = 87;

    /// <summary>Applies every part, even after one failed; the result has one bit per failed part.</summary>
    public int Apply(List<string>? details = null)
    {
        if (Path.Length == 0 || Network is { } n && !string.Equals(n.Path, Path, StringComparison.OrdinalIgnoreCase)
            || Memory is { } m && (!string.Equals(m.Path, Path, StringComparison.OrdinalIgnoreCase) || !(m.IsEmpty || m.IsValid)))
        {
            return Invalid;
        }
        int code = 0;
        // The whole-app block covers the app's helper programs too: the ones it lists after this change, and helpers
        // it no longer lists are unblocked again.
        List<string> before = NetworkPolicyStore.Find(Path)?.Helpers ?? [];
        List<string> after = Network?.Helpers ?? before;
        bool blocked = Blocked ?? WfpBlocker.ReadBlockedPaths().Contains(Path);
        void SetBlock(string path, bool block)
        {
            CommandResult result = FirewallRules.Apply(path, block);
            if (!result.Succeeded) { code |= BlockFailed; details?.Add(result.FailureSummary()); }
        }
        if (Blocked is { } block)
        {
            SetBlock(Path, block);
            foreach (string helper in after) SetBlock(helper, block);
        }
        else if (blocked)
        {
            foreach (string helper in after.Except(before, StringComparer.OrdinalIgnoreCase)) SetBlock(helper, true);
        }
        if (Blocked is not null || blocked)
        {
            foreach (string helper in before.Except(after, StringComparer.OrdinalIgnoreCase)) SetBlock(helper, false);
        }
        if (Network is not null)
        {
            CommandResult result = FirewallRules.ApplyPolicy(Network);
            if (!result.Succeeded) { code |= NetworkFailed; details?.Add(result.FailureSummary()); }
        }
        if (Memory is not null)
        {
            try
            {
                MemoryRuleStore.Save(Memory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                code |= MemoryFailed;
                details?.Add(ex.Message);
            }
        }
        return code;
    }
}
