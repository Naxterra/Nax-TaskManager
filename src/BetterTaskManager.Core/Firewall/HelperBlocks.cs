namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// Decides what happens to a helper program's whole-app block when an app that lists it is blocked or unblocked. One
/// helper (the WebView2 runtime, for example) can serve several apps, so it stays blocked while another blocked app
/// lists it, or while it was blocked on its own.
/// </summary>
public static class HelperBlocks
{
    /// <summary>True when a blocked app other than <paramref name="app"/> lists <paramref name="helper"/>.</summary>
    public static bool HasOtherBlockedOwner(string helper, string app, IEnumerable<AppNetworkPolicy> policies, IReadOnlySet<string> blocked) =>
        policies.Any(policy => !Same(policy.Path, app) && blocked.Contains(policy.Path) && policy.Helpers.Contains(helper, StringComparer.OrdinalIgnoreCase));

    /// <summary>True when unblocking <paramref name="app"/> (or dropping the helper from its list) must leave <paramref name="helper"/> blocked.</summary>
    public static bool KeepBlocked(string helper, string app, IReadOnlyList<AppNetworkPolicy> policies, IReadOnlySet<string> blocked) =>
        policies.Any(policy => Same(policy.Path, helper) && policy.BlockedOnItsOwn) || HasOtherBlockedOwner(helper, app, policies, blocked);

    /// <summary>
    /// Why <paramref name="helper"/> cannot follow <paramref name="app"/>'s rules, or null when it can: it has network
    /// rules of its own, or it already follows another app's rules. Following would replace those rules. A helper of
    /// <paramref name="replacing"/>, the older version of the same app whose rules move to <paramref name="app"/>, may follow it.
    /// </summary>
    public static string? WhyNotHelper(string helper, string app, IEnumerable<AppNetworkPolicy> policies, string? replacing = null)
    {
        if (policies.FirstOrDefault(policy => Same(policy.Path, helper)) is not { } own) return null;
        if (own.HelperOf is { } other && !Same(other, app) && !(replacing is not null && Same(other, replacing))) return $"{Path.GetFileName(helper)} already follows the rules of {Path.GetFileName(other)}.";
        if (own.HelperOf is null && own.HasFilters) return $"{Path.GetFileName(helper)} has network rules of its own.";
        return null;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
