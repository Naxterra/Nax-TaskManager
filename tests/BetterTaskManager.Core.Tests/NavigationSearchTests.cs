using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Core.Tests;

internal static class NavigationSearchTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("process_search_does_not_hide_startup_or_network", "A process-only query stays on Processes; Startup and Network receive empty queries and each tab retains its own subsequent search.", () =>
        {
            var search = new PageSearchState();
            search.Select("Processes");
            search.Edit("a-process-with-no-startup-or-network-match", fromUser: true);
            Check.Equal("", search.Select("Startup"));
            search.Edit("startup utility", fromUser: true);
            Check.Equal("", search.Select("Network"));
            search.Edit("example.test", fromUser: true);
            Check.Equal("a-process-with-no-startup-or-network-match", search.Select("Processes"));
            Check.Equal("startup utility", search.Select("Startup"));
            Check.Equal("example.test", search.Select("Network"));
        });
        yield return ("all_searchable_tabs_keep_independent_queries", "Processes, Network, Disk, History and Startup restore their own queries; clearing one page leaves the others intact.", () =>
        {
            string[] pages = ["Processes", "Network", "Disk", "History", "Startup"];
            var search = new PageSearchState();
            foreach (string page in pages)
            {
                Check.Equal("", search.Select(page));
                search.Edit(page + " query", fromUser: true);
                Check.Equal(page, search.Page);
            }
            search.Select("History");
            search.Edit("", fromUser: true);
            foreach (string page in pages.Reverse()) Check.Equal(page == "History" ? "" : page + " query", search.Select(page));
        });
        yield return ("landing_page_typing_routes_query_to_processes", "Typing on Overview, Performance, Rules or Settings still opens Processes, without retaining that text on the originating page or affecting Network.", () =>
        {
            foreach (string page in new[] { "Overview", "Performance", "Rules", "Settings" })
            {
                var search = new PageSearchState();
                search.Select(page);
                Check.Equal("Processes", search.Edit("typed query", fromUser: true));
                Check.Equal("typed query", search.Text);
                Check.Equal("", search.Select(page));
                Check.Equal(page, search.Edit("", fromUser: false));
                Check.Equal("", search.Select("Network"));
                Check.Equal("typed query", search.Select("Processes"));
            }
        });
        yield return ("contextual_navigation_changes_only_destination_query", "Overview network links set the destination's query; opening all apps clears only that destination, even when it is already selected.", () =>
        {
            var search = new PageSearchState();
            search.Select("Processes");
            search.Edit("keep process filter", fromUser: true);
            search.Select("Overview");
            search.SetForPage("Network", "203.0.113.7");
            Check.Equal("Overview", search.Page);
            Check.Equal("", search.Text);
            Check.Equal("203.0.113.7", search.Select("Network"));
            search.SetForPage("Network", "");
            Check.Equal("", search.Text);
            Check.Equal("keep process filter", search.Select("Processes"));
            Check.Equal("", search.Select("Startup"));
        });
        yield return ("startup_reload_uses_query_at_completion", "Clearing or changing a query while the background startup read is pending filters the completed data with the newest query instead of hiding it with the stale one.", () =>
        {
            foreach (string next in new[] { "", "beta" })
            {
                var search = new PageSearchState();
                search.Select("Startup");
                search.Edit("absent", fromUser: true);
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                string[] visible = [];
                Task load = SearchableListLoader.LoadAsync(() =>
                {
                    entered.Set();
                    Check.That(release.Wait(5000), "startup read gate timed out");
                    return new[] { "alpha", "beta" };
                }, () => search.GetForPage("Startup"), (apps, query) => visible = apps.Where(app => app.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
                try
                {
                    Check.That(entered.Wait(5000), "startup read never entered");
                    search.Edit(next, fromUser: true);
                }
                finally { release.Set(); }
                Check.That(load.Wait(5000), "startup read never completed");
                Check.That(visible.SequenceEqual(next.Length == 0 ? ["alpha", "beta"] : ["beta"]), "completed list used a stale search query");
            }
        });
        yield return ("inactive_startup_reload_keeps_its_own_query", "Switching to Network while Startup is reading does not apply the network filter to the cached startup list.", () =>
        {
            var search = new PageSearchState();
            search.Select("Startup");
            search.Edit("startup query", fromUser: true);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            string? applied = null;
            Task load = SearchableListLoader.LoadAsync(() =>
            {
                entered.Set();
                Check.That(release.Wait(5000), "startup read gate timed out");
                return 24;
            }, () => search.GetForPage("Startup"), (_, query) => applied = query);
            try
            {
                Check.That(entered.Wait(5000), "startup read never entered");
                search.Select("Network");
                search.Edit("network query", fromUser: true);
            }
            finally { release.Set(); }
            Check.That(load.Wait(5000), "startup read never completed");
            Check.Equal("startup query", applied);
            Check.Equal("network query", search.Text);
        });
    }
}
