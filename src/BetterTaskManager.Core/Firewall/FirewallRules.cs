using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// Outbound per-executable block rules. Rule names are identical to the WinForms build's, so rules created by
/// either app are recognised by the other.
/// </summary>
public static partial class FirewallRules
{
    private const string RulePrefix = "BetterTaskManager Block ";

    [GeneratedRegex(@"BetterTaskManager Block [0-9A-F]{12}", RegexOptions.IgnoreCase)]
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

    /// <summary>
    /// Returns the names of all Better Task Manager block rules. Matching rule names instead of parsing field
    /// labels keeps this independent of the Windows display language.
    /// </summary>
    public static HashSet<string> ReadBlockRuleNames()
    {
        CommandResult result = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=all", "dir=out");
        if (!result.Succeeded) throw new InvalidOperationException(result.FailureSummary());
        return RuleNamePattern().Matches(result.StandardOutput)
            .Select(match => match.Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Runs netsh directly. The caller must be elevated.</summary>
    public static CommandResult Apply(string path, bool block)
    {
        if (string.IsNullOrWhiteSpace(path)) return new CommandResult(87, "", "The executable path is empty.", false);
        string rule = RuleNameForPath(path);
        if (!block) return CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule);

        // Remove any previous copy first so repeated blocks never create duplicate rules.
        CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule);
        return CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + rule,
            "dir=out", "program=" + path, "action=block", "profile=any");
    }
}
