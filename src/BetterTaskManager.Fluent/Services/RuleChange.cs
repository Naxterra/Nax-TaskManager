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
        // One change at a time across all processes, so another elevated helper or window cannot undo part of it.
        using IDisposable rulesLock = Core.StoreLock.Take(Core.StoreLock.Rules);
        if (Path.Length == 0 || Network is { } n && !string.Equals(n.Path, Path, StringComparison.OrdinalIgnoreCase)
            || Memory is { } m && (!string.Equals(m.Path, Path, StringComparison.OrdinalIgnoreCase) || !(m.IsEmpty || m.IsValid))
            || ReplacesPath is { } old && (!System.IO.Path.IsPathFullyQualified(old) || string.Equals(old, Path, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid;
        }
        int code = 0;
        List<AppNetworkPolicy> policies;
        string? rulesUnreadable = null;
        try
        {
            // Read strictly: with a damaged rules file the helper checks below would see nothing, so nothing changes.
            policies = NetworkPolicyStore.ReadForUpdate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // A plain block or unblock from the Processes or Network page still works on the program itself, but the
            // helpers it may list cannot be read, which the result reports. Anything that would write the rules waits
            // for a reset.
            if (Network is not null || ReplacesPath is not null || Blocked is null)
            {
                details?.Add(ex.Message);
                return NetworkFailed;
            }
            policies = [];
            rulesUnreadable = ex.Message;
        }
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
        // Only a block the user sets on the helper itself is marked as its own (see FirewallRules.ApplyOwn). A helper that
        // is already blocked for another reason keeps that block, and it is lifted with this app's.
        // Helpers this change blocked, so they can be released again when the rules that list them are not saved.
        var newlyBlocked = new List<string>();
        void BlockHelper(string helper)
        {
            if (blockedPaths.Contains(helper)) return;
            SetBlock(helper, true);
            newlyBlocked.Add(helper);
            // A helper that was not blocked cannot be blocked on its own. A mark left from an unblock while the rules
            // could not be written would otherwise keep it blocked after this app is unblocked.
            if (policies.Any(policy => string.Equals(policy.Path, helper, StringComparison.OrdinalIgnoreCase) && policy.BlockedOnItsOwn))
            {
                try
                {
                    NetworkPolicyStore.SetBlockedOnItsOwn(helper, false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    code |= BlockFailed;
                    details?.Add(ex.Message);
                }
            }
        }
        // A helper stays blocked while another blocked app lists it (one WebView2 runtime serves many apps), or while
        // it was blocked on its own.
        void UnblockHelper(string helper)
        {
            if (!HelperBlocks.KeepBlocked(helper, Path, policies, blockedPaths)) SetBlock(helper, false);
        }
        // Helpers dropped from the list are released only once the new list is stored. If storing it fails, the
        // stored rules still list them for the blocked app, so they stay blocked to match.
        var dropped = new List<string>();
        if (Blocked is { } block)
        {
            // Blocking a program another app lists as a helper is a block of its own, and unblocking it ends that.
            CommandResult own = FirewallRules.ApplyOwn(Path, block);
            if (!own.Succeeded) { code |= BlockFailed; details?.Add(own.FailureSummary()); }
            if (block)
            {
                foreach (string helper in wasBlocked ? after.Except(before, StringComparer.OrdinalIgnoreCase) : after) BlockHelper(helper);
                dropped.AddRange(before.Except(after, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Only the helpers this app listed so far were blocked with it. A newly listed one keeps its own state.
                foreach (string helper in before) UnblockHelper(helper);
            }
        }
        else if (blocked)
        {
            foreach (string helper in after.Except(before, StringComparer.OrdinalIgnoreCase)) BlockHelper(helper);
            dropped.AddRange(before.Except(after, StringComparer.OrdinalIgnoreCase));
        }
        if (Network is not null)
        {
            CommandResult result = FirewallRules.ApplyPolicy(Network, ReplacesPath);
            if (!result.Succeeded)
            {
                code |= NetworkFailed;
                details?.Add(result.FailureSummary());
                // The helper list was not stored, so nothing would ever lift the blocks this change gave the new helpers.
                foreach (string helper in newlyBlocked.Where(helper => !before.Contains(helper, StringComparer.OrdinalIgnoreCase))) SetBlock(helper, false);
            }
            else
            {
                foreach (string helper in dropped) UnblockHelper(helper);
            }
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
        if (rulesUnreadable is not null)
        {
            code |= NetworkFailed;
            details?.Add("The block of the program itself was changed, but its helper programs were not, because the stored rules could not be read (" + rulesUnreadable + "). Reset the rules on the Rules page.");
        }
        return code;
    }
}
