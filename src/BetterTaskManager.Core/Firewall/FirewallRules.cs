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
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        Exception? netshError = null;
        CommandResult result = CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "show", "rule", "name=all", "dir=out");
        if (result.Succeeded)
        {
            foreach (Match match in RuleNamePattern().Matches(result.StandardOutput))
            {
                names.Add(RulePrefix + match.Groups[1].Value.ToUpperInvariant());
            }
        }
        else
        {
            netshError = new InvalidOperationException(result.FailureSummary());
        }
        // Blocks made since WFP enforcement are filters, not netsh rules; report them under the same names.
        // Read WFP even when netsh fails (for example when Windows Firewall is disabled or managed by another product).
        try
        {
            foreach (string path in WfpBlocker.ReadBlockedPaths()) names.Add(RuleNameForPath(path));
        }
        catch (Exception) when (netshError is not null)
        {
            throw netshError;
        }
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
        using IDisposable rulesLock = StoreLock.Take(StoreLock.Rules);
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

        // The filter applies to new outbound connections at once; close open outbound ones too, leaving inbound alone.
        if (block) TryCut(path, (inbound, _, _) => !inbound);

        string rule = RuleNameForPath(path);
        string[] legacy = [LegacyRuleName(rule), rule];
        foreach (string name in legacy) CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "delete", "rule", "name=" + name);
        if (!block && legacy.FirstOrDefault(RuleExists) is { } remaining)
        {
            return new CommandResult(1, "", $"The Windows Firewall rule \"{remaining}\" from an earlier version could not be removed.", false);
        }
        return new CommandResult(0, "", "", false);
    }

    /// <summary>
    /// <see cref="Apply"/> for a block the user sets on this program itself. The block is marked as its own, so if an
    /// app lists the program as a helper now or later, unblocking that app leaves it in place. Unblocking clears the
    /// mark. With a damaged rules file the block itself still changes, and only the mark cannot be stored.
    /// </summary>
    public static CommandResult ApplyOwn(string path, bool block)
    {
        using IDisposable rulesLock = StoreLock.Take(StoreLock.Rules);
        bool rulesReadable;
        try
        {
            NetworkPolicyStore.ReadForUpdate();
            rulesReadable = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            rulesReadable = false;
        }
        CommandResult result = Apply(path, block);
        if (!result.Succeeded || !rulesReadable) return result;
        try
        {
            NetworkPolicyStore.SetBlockedOnItsOwn(path, block);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CommandResult(1, "", ex.Message, false);
        }
        return result;
    }

    /// <summary>
    /// Applies one app's rules: the WFP filters first (one transaction), then the stored copy the app reads, then
    /// open connections the new rules block are closed. An empty policy removes the app's rules. Must be elevated.
    /// </summary>
    public static CommandResult ApplyPolicy(AppNetworkPolicy policy, string? replacing = null)
    {
        using IDisposable rulesLock = StoreLock.Take(StoreLock.Rules);
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
        var applied = new List<(string Target, AppNetworkPolicy Previous)>();
        try
        {
            // Read the stored rules strictly before any filter changes: a damaged file stops the change here, while
            // filters and file still agree.
            List<AppNetworkPolicy> stored = NetworkPolicyStore.ReadForUpdate();
            AppNetworkPolicy? previous = stored.FirstOrDefault(entry => string.Equals(entry.Path, policy.Path, StringComparison.OrdinalIgnoreCase));
            // A newly listed helper would get this app's rules in place of its own, or of the app it already follows.
            foreach (string added in helpers.Except(previous?.Helpers ?? [], StringComparer.OrdinalIgnoreCase))
            {
                if (HelperBlocks.WhyNotHelper(added, policy.Path, stored, replacing) is { } reason) return new CommandResult(87, "", reason, false);
            }

            void ApplyTracked(AppNetworkPolicy target)
            {
                AppNetworkPolicy prior = stored.FirstOrDefault(entry => string.Equals(entry.Path, target.Path, StringComparison.OrdinalIgnoreCase))
                    ?? new AppNetworkPolicy { Path = target.Path };
                WfpBlocker.ApplyPolicy(target);
                applied.Add((target.Path, prior));
                NetworkPolicyStore.Save(target);
            }

            ApplyTracked(policy);
            // Each helper gets the app's switches and rules as its own filters, marked as the app's helper.
            foreach (string helper in helpers)
            {
                // Kept even without filters, so the helper stays linked to this app and no other app takes it over.
                ApplyTracked(policy.ForHelper(helper));
            }
            // A helper the app no longer lists loses the rules it had from the app, never rules of its own.
            foreach (string dropped in previous?.Helpers.Except(helpers, StringComparer.OrdinalIgnoreCase) ?? [])
            {
                AppNetworkPolicy? own = stored.FirstOrDefault(entry => string.Equals(entry.Path, dropped, StringComparison.OrdinalIgnoreCase));
                if (own is not null && !string.Equals(own.HelperOf, policy.Path, StringComparison.OrdinalIgnoreCase)) continue;
                ApplyTracked(new AppNetworkPolicy { Path = dropped });
            }
        }
        catch (Exception ex) when (ex is WfpException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Roll back any filters and stored entries written before the failure so filters and app-rules.json stay aligned.
            for (int index = applied.Count - 1; index >= 0; index--)
            {
                AppNetworkPolicy prior = applied[index].Previous;
                try { WfpBlocker.ApplyPolicy(prior); } catch (Exception rollback) when (rollback is WfpException or IOException or UnauthorizedAccessException) { }
                try { NetworkPolicyStore.Save(prior); } catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
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
        CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "show", "rule", "name=" + name).ExitCode == 0;

    /// <summary>
    /// Recovery for a damaged rules file: removes every rule filter of every app and the file, so filters and file
    /// agree again (no rules). Whole-app blocks stay, because WFP itself lists them. Must be elevated.
    /// </summary>
    public static CommandResult ResetPolicies()
    {
        using IDisposable rulesLock = StoreLock.Take(StoreLock.Rules);
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
        using IDisposable rulesLock = StoreLock.Take(StoreLock.Rules);
        WfpBlocker.RemoveAll();
        NetworkPolicyStore.DeleteAll();
        CommandResult result = CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "show", "rule", "name=all", "dir=out");
        if (!result.Succeeded) return;
        foreach (string hash in RuleNamePattern().Matches(result.StandardOutput).Select(match => match.Groups[1].Value.ToUpperInvariant()).Distinct())
        {
            CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "delete", "rule", "name=" + RulePrefix + hash);
            CommandRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall", "firewall", "delete", "rule", "name=" + LegacyRulePrefix + hash);
        }
    }
}
