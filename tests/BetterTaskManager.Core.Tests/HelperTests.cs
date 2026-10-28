using System.Diagnostics;
using BetterTaskManager.Core.Firewall;

namespace BetterTaskManager.Core.Tests;

/// <summary>Helper programs shared by several apps, and the guard around deleting the data folder.</summary>
internal static class HelperTests
{
    private const string Runtime = @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\130.0.1\msedgewebview2.exe";
    private const string Teams = @"C:\Apps\Teams.exe";
    private const string Outlook = @"C:\Apps\Outlook.exe";

    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("shared_helper_stays_blocked_while_another_blocked_app_lists_it",
            "Teams and Outlook both list the WebView2 runtime and both are blocked. Unblocking Teams keeps the runtime blocked for Outlook, and once Outlook is unblocked too, the runtime is unblocked.",
            () =>
            {
                List<AppNetworkPolicy> policies = [new() { Path = Teams, Helpers = [Runtime] }, new() { Path = Outlook, Helpers = [Runtime] }];
                var blocked = new HashSet<string>([Teams, Outlook, Runtime], StringComparer.OrdinalIgnoreCase);
                Check.That(HelperBlocks.KeepBlocked(Runtime, Teams, policies, blocked), "Outlook still needs the runtime blocked");
                blocked.Remove(Outlook);
                Check.That(!HelperBlocks.KeepBlocked(Runtime, Teams, policies, blocked), "with no other blocked app the runtime is unblocked");
            });

        yield return ("helper_blocked_on_its_own_survives_unblocking_the_app",
            "The runtime was already blocked before Teams was blocked. That block counts as its own, and unblocking Teams later leaves the runtime blocked.",
            () =>
            {
                List<AppNetworkPolicy> policies = [new() { Path = Teams, Helpers = [Runtime] }];
                var blocked = new HashSet<string>([Runtime], StringComparer.OrdinalIgnoreCase);
                Check.That(HelperBlocks.WasBlockedOnItsOwn(Runtime, Teams, policies, blocked), "a block that no app explains is the helper's own");
                policies.Add(new AppNetworkPolicy { Path = Runtime, BlockedOnItsOwn = true });
                blocked.Add(Teams);
                Check.That(HelperBlocks.KeepBlocked(Runtime, Teams, policies, blocked), "the helper's own block must outlive the app's");
            });

        yield return ("helper_with_own_rules_or_another_app_is_refused",
            "A program with network rules of its own, or one that already follows Outlook, cannot become a helper of Teams, while a program without rules can.",
            () =>
            {
                List<AppNetworkPolicy> policies =
                [
                    new() { Path = @"C:\Apps\Own.exe", BlockLan = true },
                    new() { Path = Runtime, HelperOf = Outlook, BlockInternet = true },
                ];
                Check.That(HelperBlocks.WhyNotHelper(@"C:\Apps\Own.exe", Teams, policies) is not null, "own rules must be refused");
                Check.That(HelperBlocks.WhyNotHelper(Runtime, Teams, policies) is not null, "another app's helper must be refused");
                Check.That(HelperBlocks.WhyNotHelper(Runtime, Outlook, policies) is null, "the app it already follows may keep it");
                Check.That(HelperBlocks.WhyNotHelper(@"C:\Apps\Free.exe", Teams, policies) is null, "a program without rules may become a helper");
            });

        yield return ("own_block_mark_survives_saving_the_helpers_rules",
            "The runtime's entry is marked as blocked on its own. Saving rules from Teams over that entry, and then saving an empty entry, both keep the mark, and clearing it removes the entry.",
            () => StoreTests.InTempFolder(folder =>
            {
                string file = Path.Combine(folder, "app-rules.json");
                NetworkPolicyStore.SetBlockedOnItsOwn(Runtime, true, file);
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = Runtime, HelperOf = Teams, BlockLan = true }, file);
                Check.That(NetworkPolicyStore.Read(file).Single().BlockedOnItsOwn, "saving derived rules must keep the mark");
                NetworkPolicyStore.Save(new AppNetworkPolicy { Path = Runtime }, file);
                Check.That(NetworkPolicyStore.Read(file).Single().BlockedOnItsOwn, "an empty save must keep the marked entry");
                NetworkPolicyStore.SetBlockedOnItsOwn(Runtime, false, file);
                Check.Equal(0, NetworkPolicyStore.Read(file).Count);
            }));

        yield return ("data_folder_delete_removes_only_a_link_and_refuses_a_foreign_folder",
            "Deleting a data folder that is a junction removes only the junction and leaves its target with its file, and a folder owned by the user (not made by the app) is refused and kept.",
            () => StoreTests.InTempFolder(folder =>
            {
                string target = Path.Combine(folder, "target");
                string link = Path.Combine(folder, "data");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "keep.txt"), "x");
                using (Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                {
                    mklink.WaitForExit();
                }
                Check.That(Directory.Exists(link), "the test junction could not be created");
                DataFolder.DeleteAll(link);
                Check.That(!Directory.Exists(link) && File.Exists(Path.Combine(target, "keep.txt")), "only the junction may go");
                bool refused = false;
                try
                {
                    DataFolder.DeleteAll(target);
                }
                catch (UnauthorizedAccessException)
                {
                    refused = true;
                }
                Check.That(refused && File.Exists(Path.Combine(target, "keep.txt")), "a folder the app did not make must be kept");
            }));
    }
}
