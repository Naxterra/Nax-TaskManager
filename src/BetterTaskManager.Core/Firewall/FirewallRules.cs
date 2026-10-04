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
        return RuleNamePattern().Matches(result.StandardOutput)
            .Select(match => RulePrefix + match.Groups[1].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Runs netsh directly. The caller must be elevated.</summary>
    public static CommandResult Apply(string path, bool block)
    {
        if (string.IsNullOrWhiteSpace(path)) return new CommandResult(87, "", "The executable path is empty.", false);
        string rule = RuleNameForPath(path);
        // Remove both names first: unblocking must also clear a pre-rename rule, and repeated blocks never duplicate.
        CommandResult removedLegacy = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + LegacyRuleName(rule));
        CommandResult removed = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule);
        if (!block) return removed.Succeeded ? removed : removedLegacy;

        return CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + rule,
            "dir=out", "program=" + path, "action=block", "profile=any");
    }
}
