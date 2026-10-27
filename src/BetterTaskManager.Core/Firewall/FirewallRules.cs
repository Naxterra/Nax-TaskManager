using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// Outbound per-executable block rules named "Nax-TaskManager Block &lt;hash&gt;". Rules from before the rename
/// ("BetterTaskManager Block &lt;hash&gt;", same hash) still count as blocking and are removed on unblock.
/// </summary>
public static partial class FirewallRules
{
    private const string RulePrefix = "Nax-TaskManager Block ";
    private const string LegacyRulePrefix = "BetterTaskManager Block ";

    [GeneratedRegex(@"(?:Nax-TaskManager|BetterTaskManager) Block ([0-9A-F]{12})", RegexOptions.IgnoreCase)]
    private static partial Regex RuleNamePattern();

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string RuleNameForPath(string path)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return RulePrefix + Convert.ToHexString(hash)[..12];
    }

    private static string LegacyRuleName(string rule) => LegacyRulePrefix + rule[RulePrefix.Length..];

    /// <summary>
    /// Returns the names of all block rules, legacy ones under their current name. Matching rule names instead of
    /// parsing field labels keeps this independent of the Windows display language.
    /// </summary>
    public static HashSet<string> ReadBlockRuleNames()
    {
        CommandResult result = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=all", "dir=out");
        if (!result.Succeeded) throw new InvalidOperationException(result.FailureSummary());
        HashSet<string> names = RuleNamePattern().Matches(result.StandardOutput)
            .Select(match => RulePrefix + match.Groups[1].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Blocks made since WFP enforcement are filters, not netsh rules; report them under the same names.
        foreach (string path in WfpBlocker.ReadBlockedPaths()) names.Add(RuleNameForPath(path));
        return names;
    }

    /// <summary>
    /// Blocks or allows outbound traffic for an executable. Enforcement is a Windows Filtering Platform filter, which
    /// also works while a third-party firewall has turned Windows Firewall rules off. Windows Firewall rules from
    /// earlier versions (either name) are removed only after the filter change took effect, so a failed block never
    /// leaves the program less protected than before; an unblock fails if such a rule survives. Must be elevated.
    /// </summary>
    public static CommandResult Apply(string path, bool block)
    {
        // Only full paths: a relative path or a bare name would block whatever the helper's working folder resolves it to.
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return new CommandResult(87, "", "The executable path is not a full path.", false);
        try
        {
            if (block) WfpBlocker.Block(path);
            else WfpBlocker.Unblock(path);
        }
        catch (Exception ex) when (ex is WfpException or IOException or UnauthorizedAccessException)
        {
            return new CommandResult(ex is WfpException wfp ? (int)wfp.Code : 1, "", ex.Message, false);
        }

        // The filter applies to new connections at once; close the open ones too, so the block is immediate.
        if (block) TryCut(path, (_, _, _) => true);

        string rule = RuleNameForPath(path);
        string[] legacy = [LegacyRuleName(rule), rule];
        foreach (string name in legacy) CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + name);
        if (!block && legacy.FirstOrDefault(RuleExists) is { } remaining)
        {
            return new CommandResult(1, "", $"The Windows Firewall rule \"{remaining}\" from an earlier version could not be removed.", false);
        }
        return new CommandResult(0, "", "", false);
    }

    /// <summary>
    /// Applies one app's rules: the WFP filters first (one transaction), then the stored copy the app reads, then
    /// open connections the new rules block are closed. An empty policy removes the app's rules. Must be elevated.
    /// </summary>
    public static CommandResult ApplyPolicy(AppNetworkPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Path) || !Path.IsPathFullyQualified(policy.Path))
        {
            return new CommandResult(87, "", "The executable path is not a full path.", false);
        }
        var errors = new List<string>();
        NetworkRule.ParseList(string.Join('\n', policy.Outgoing), errors);
        NetworkRule.ParseList(string.Join('\n', policy.Incoming), errors);
        if (errors.Count > 0) return new CommandResult(87, "", "Invalid rule: " + string.Join("; ", errors), false);
        List<string> helpers = policy.Helpers.Where(helper => Path.IsPathFullyQualified(helper) && !string.Equals(helper, policy.Path, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        policy.Helpers = helpers;
        try
        {
            // Read the stored rules strictly before any filter changes: a damaged file stops the change here, while
            // filters and file still agree.
            AppNetworkPolicy? previous = NetworkPolicyStore.ReadForUpdate()
                .FirstOrDefault(stored => string.Equals(stored.Path, policy.Path, StringComparison.OrdinalIgnoreCase));
            WfpBlocker.ApplyPolicy(policy);
            NetworkPolicyStore.Save(policy);
            // Each helper gets the app's switches and rules as its own filters, marked as the app's helper.
            foreach (string helper in helpers)
            {
                AppNetworkPolicy derived = policy.ForHelper(helper);
                WfpBlocker.ApplyPolicy(derived);
                NetworkPolicyStore.Save(derived.HasFilters ? derived : new AppNetworkPolicy { Path = helper });
            }
            // A helper the app no longer lists loses the rules it had from the app, never rules of its own.
            foreach (string dropped in previous?.Helpers.Except(helpers, StringComparer.OrdinalIgnoreCase) ?? [])
            {
                if (NetworkPolicyStore.Find(dropped) is { } own && !string.Equals(own.HelperOf, policy.Path, StringComparison.OrdinalIgnoreCase)) continue;
                var empty = new AppNetworkPolicy { Path = dropped };
                WfpBlocker.ApplyPolicy(empty);
                NetworkPolicyStore.Save(empty);
            }
        }
        catch (Exception ex) when (ex is WfpException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CommandResult(ex is WfpException wfp ? (int)wfp.Code : 1, "", ex.Message, false);
        }
        foreach (string path in helpers.Prepend(policy.Path)) TryCut(path, (inbound, remote, port) => policy.Evaluate(inbound, remote, 6, port) is not null);
        return new CommandResult(0, "", "", false);
    }

    private static void TryCut(string path, Func<bool, System.Net.IPAddress, int, bool> blocked)
    {
        try
        {
            ConnectionCutter.Cut(path, blocked);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // The filters already block; closing open connections only makes it faster.
        }
    }

    private static bool RuleExists(string name) =>
        CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=" + name).ExitCode == 0;

    /// <summary>
    /// Recovery for a damaged rules file: removes every rule filter of every app and the file, so filters and file
    /// agree again (no rules). Whole-app blocks stay, because WFP itself lists them. Must be elevated.
    /// </summary>
    public static CommandResult ResetPolicies()
    {
        try
        {
            WfpBlocker.RemoveAllPolicies();
            NetworkPolicyStore.DeleteAll();
            return new CommandResult(0, "", "", false);
        }
        catch (Exception ex) when (ex is WfpException or IOException or UnauthorizedAccessException)
        {
            return new CommandResult(ex is WfpException wfp ? (int)wfp.Code : 1, "", ex.Message, false);
        }
    }

    /// <summary>Removes every block and rule this app ever made: WFP filters, the stored rules and Windows Firewall rules of earlier versions.</summary>
    public static void RemoveAll()
    {
        WfpBlocker.RemoveAll();
        NetworkPolicyStore.DeleteAll();
        CommandResult result = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=all", "dir=out");
        if (!result.Succeeded) return;
        foreach (string hash in RuleNamePattern().Matches(result.StandardOutput).Select(match => match.Groups[1].Value.ToUpperInvariant()).Distinct())
        {
            CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + RulePrefix + hash);
            CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + LegacyRulePrefix + hash);
        }
    }
}
