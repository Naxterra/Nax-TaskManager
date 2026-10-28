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
    /// <summary>
    /// The program file of an older version of this app that no longer exists. After the change is applied to
    /// <see cref="Path"/>, everything stored for that file is removed, so its rules move to the new file in one step.
    /// </summary>
    public string? ReplacesPath { get; set; }

    public const int BlockFailed = 1, NetworkFailed = 2, MemoryFailed = 4, Invalid = 87;

    /// <summary>Applies every part, even after one failed; the result has one bit per failed part.</summary>
    public int Apply(List<string>? details = null)
    {
        if (Path.Length == 0 || Network is { } n && !string.Equals(n.Path, Path, StringComparison.OrdinalIgnoreCase)
            || Memory is { } m && (!string.Equals(m.Path, Path, StringComparison.OrdinalIgnoreCase) || !(m.IsEmpty || m.IsValid))
            || ReplacesPath is { } old && (!System.IO.Path.IsPathFullyQualified(old) || string.Equals(old, Path, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid;
        }
        int code = 0;
        List<AppNetworkPolicy> policies = NetworkPolicyStore.Load();
        // The whole-app block covers the app's helper programs too: the ones it lists after this change, and helpers
        // it no longer lists are unblocked again.
        List<string> before = policies.FirstOrDefault(policy => string.Equals(policy.Path, Path, StringComparison.OrdinalIgnoreCase))?.Helpers ?? [];
        List<string> after = Network?.Helpers ?? before;
        // A helper the rules cannot take on is refused before anything changes, so no block is left without the list
        // that would lift it again.
        foreach (string added in after.Except(before, StringComparer.OrdinalIgnoreCase))
        {
            if (HelperBlocks.WhyNotHelper(added, Path, policies, ReplacesPath) is { } reason)
            {
                details?.Add(reason);
                return Invalid;
            }
        }
        HashSet<string> blockedPaths = WfpBlocker.ReadBlockedPaths();
        bool wasBlocked = blockedPaths.Contains(Path);
        bool blocked = Blocked ?? wasBlocked;
        void SetBlock(string path, bool block)
        {
            CommandResult result = FirewallRules.Apply(path, block);
            if (!result.Succeeded) { code |= BlockFailed; details?.Add(result.FailureSummary()); }
        }
        void MarkOwn(string path, bool value)
        {
            try
            {
                NetworkPolicyStore.SetBlockedOnItsOwn(path, value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                code |= BlockFailed;
                details?.Add(ex.Message);
            }
        }
        // A helper that is already blocked although no other blocked app lists it was blocked on its own. That block
        // has to outlive this app's.
        void BlockHelper(string helper)
        {
            if (!blockedPaths.Contains(helper)) SetBlock(helper, true);
            else if (HelperBlocks.WasBlockedOnItsOwn(helper, Path, policies, blockedPaths)) MarkOwn(helper, true);
        }
        // A helper stays blocked while another blocked app lists it (one WebView2 runtime serves many apps), or while
        // it was blocked on its own.
        void UnblockHelper(string helper)
        {
            if (!HelperBlocks.KeepBlocked(helper, Path, policies, blockedPaths)) SetBlock(helper, false);
        }
        if (Blocked is { } block)
        {
            // Blocking a program another app lists as a helper is a block of its own, and unblocking it ends that.
            CommandResult own = FirewallRules.ApplyOwn(Path, block);
            if (!own.Succeeded) { code |= BlockFailed; details?.Add(own.FailureSummary()); }
            if (block)
            {
                foreach (string helper in wasBlocked ? after.Except(before, StringComparer.OrdinalIgnoreCase) : after) BlockHelper(helper);
                foreach (string helper in before.Except(after, StringComparer.OrdinalIgnoreCase)) UnblockHelper(helper);
            }
            else
            {
                foreach (string helper in before.Union(after, StringComparer.OrdinalIgnoreCase)) UnblockHelper(helper);
            }
        }
        else if (blocked)
        {
            foreach (string helper in after.Except(before, StringComparer.OrdinalIgnoreCase)) BlockHelper(helper);
            foreach (string helper in before.Except(after, StringComparer.OrdinalIgnoreCase)) UnblockHelper(helper);
        }
        if (Network is not null)
        {
            CommandResult result = FirewallRules.ApplyPolicy(Network, ReplacesPath);
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
        // Only once the new file has everything: the old one's block, rules and memory rule go. Its helpers stay
        // blocked, because the new file lists them now.
        if (code == 0 && ReplacesPath is { } replaced)
        {
            var removal = new RuleChange
            {
                Path = replaced,
                Blocked = WfpBlocker.ReadBlockedPaths().Contains(replaced) ? false : null,
                Network = new AppNetworkPolicy { Path = replaced },
                Memory = new AppMemoryRule { Path = replaced }
            };
            code |= removal.Apply(details);
        }
        return code;
    }
}
