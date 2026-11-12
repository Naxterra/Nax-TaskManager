using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net;
using System.Diagnostics;
using BetterTaskManager.Core.Feed;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;
using BetterTaskManager.Fluent.Services;

namespace BetterTaskManager.Core.Tests;

internal static class NetworkRegressionTests
{
    public static IEnumerable<(string Name, string Scenario, Action Body)> All()
    {
        yield return ("every_network_column_can_move_to_the_first_position", "Every connection column can replace Remote host at grid position zero, move back to the end, and retain its saved position.", () =>
        {
            string[] columns = ["Remote", "App", "Local", "Scope", "State", "Download", "Upload"];
            foreach (string column in columns)
            {
                var order = new ColumnOrderState(columns, null, movableFirst: true);
                order.Move(column, int.MinValue);
                Check.Equal(0, order.PositionOf(column));
                Check.Equal(column, order.ColumnAt(0));
                Check.Equal(7, order.Columns.Distinct().Count());
                var loaded = new ColumnOrderState(columns, string.Join(",", order.Columns), movableFirst: true);
                Check.That(order.Columns.SequenceEqual(loaded.Columns), "saved first position lost");
                loaded.Move(column, int.MaxValue);
                Check.Equal(6, loaded.PositionOf(column));
                loaded.Reset();
                Check.That(loaded.IsDefault, "reset did not restore Remote host first");
            }
            var legacy = new ColumnOrderState(columns, "Upload,Download,App,Local,Scope,State", movableFirst: true);
            Check.Equal("Remote,Upload,Download,App,Local,Scope,State", string.Join(",", legacy.Columns));
        });
        yield return ("column_drop_geometry_includes_the_leftmost_column", "Drop hit testing, indicator edges and width lookup follow the displayed order, including resized columns at position zero.", () =>
        {
            var widths = new Dictionary<string, double> { ["Remote"] = 300, ["App"] = 180, ["Upload"] = 130 };
            var order = new ColumnOrderState(["Remote", "App", "Upload"], null, movableFirst: true);
            Check.Equal(0, order.PositionAt(-10, widths, 0));
            Check.Equal(0, order.PositionAt(299, widths, 0));
            Check.Equal(1, order.PositionAt(300, widths, 0));
            order.Move("Upload", 0);
            Check.Equal(0d, order.LeftEdgeOf(0, widths, 0));
            Check.Equal(130d, order.LeftEdgeOf(1, widths, 0));
            Check.Equal(0, order.PositionAt(129, widths, 0));
            Check.Equal(1, order.PositionAt(130, widths, 0));
            widths["Upload"] = 210;
            Check.Equal(210d, order.LeftEdgeOf(1, widths, 0));
            Check.Equal(0, order.PositionAt(209, widths, 0));
            Check.Equal(2, order.PositionAt(9000, widths, 0));
            Check.Equal("Upload", order.ColumnAt(0));
        });
        yield return ("other_tables_keep_their_fixed_first_column", "Tables that pin their name column keep one-based positions, their existing saved order, leading width and move limits.", () =>
        {
            var order = new ColumnOrderState(["Cpu", "Memory"], "Memory,Cpu");
            var widths = new Dictionary<string, double> { ["Cpu"] = 100, ["Memory"] = 150 };
            Check.Equal(1, order.FirstPosition);
            Check.Equal(2, order.LastPosition);
            Check.Equal(1, order.PositionOf("Memory"));
            Check.Equal(300d, order.LeftEdgeOf(1, widths, 300));
            Check.Equal(1, order.PositionAt(0, widths, 300));
            Check.Equal(2, order.PositionAt(450, widths, 300));
            Check.That(order.Move("Cpu", 0), "move to first movable position failed");
            Check.Equal(1, order.PositionOf("Cpu"));
            Check.That(order.ColumnAt(0) is null, "pinned column was replaced");
            Check.That(!order.Move("Name", 0), "pinned name column became movable");
            Check.That(!new ColumnOrderState([], null).Move("Unknown", 0), "unknown column accepted");
        });
        yield return ("split_traffic_columns_preserve_layout", "A saved combined-speed column becomes adjacent Download and Upload columns; an explicit new width is preserved.", () =>
        {
            var settings = new AppSettings { NetworkConnectionSortColumn = "Speed", NetworkConnectionSortDescending = true };
            settings.ColumnOrder["Connections."] = "Speed,App,Local,Scope,State";
            settings.ColumnWidths["Connections.Speed"] = 300;
            settings.ColumnWidths["Connections.Upload"] = 190;
            var sorting = new NetworkSortState(settings);
            Check.Equal("Download", sorting.ConnectionColumn);
            Check.That(sorting.ConnectionsDescending, "old descending order lost");
            Check.Equal("Download,Upload,App,Local,Scope,State", settings.ColumnOrder["Connections."]);
            Check.Equal(150d, settings.ColumnWidths["Connections.Download"]);
            Check.Equal(190d, settings.ColumnWidths["Connections.Upload"]);
        });
        yield return ("udp_receive_and_send_share_the_observed_peer", "IPv4 and IPv6 receive packets reverse packet endpoints into the local/remote socket identity; byte directions stay correct.", () =>
        {
            foreach (var (local, remote) in new[] { ("192.0.2.10", "203.0.113.7"), ("2001:db8::10", "2001:db8::7") })
            {
                using var bandwidth = new BandwidthMonitor("NaxTests-Udp-NoTrace");
                bandwidth.AddUdpPacket(42, false, 100, IPAddress.Parse(local), 50000, IPAddress.Parse(remote), 443);
                bandwidth.AddUdpPacket(42, true, 300, IPAddress.Parse(remote), 443, IPAddress.Parse(local), 50000);
                var counts = bandwidth.Drain();
                Check.Equal(1, counts.Sockets.Count);
                Check.Equal(50000, counts.Sockets.Single().Key.LocalPort);
                Check.Equal(remote, counts.Sockets.Single().Key.RemoteAddress.ToString());
                Check.Equal(300L, counts.Sockets.Single().Value.Received);
                Check.Equal(100L, counts.Sockets.Single().Value.Sent);
            }
        });
        yield return ("udp_peers_round_trip_without_duplicating_socket_rates", "Per-peer endpoints and directional rates survive JSON and smoothing without being added to the existing aggregate socket rates.", () =>
        {
            var process = UdpProcess();
            var peers = UdpPeers.FromFlows([process], [UdpFlow("203.0.113.7", 300, 100), UdpFlow("203.0.113.8", 100, 300)], 2);
            Check.Equal(2, peers.Count);
            var aggregate = UdpSocket("::", 200, 200);
            var snapshot = new MonitorSnapshot
            {
                Timestamp = DateTime.Now, Processes = [process], Connections = [aggregate], UdpPeers = peers,
                NetworkIssues = [], System = new SystemSample { Memory = new MemoryBreakdown(), PerProcessNetworkAvailable = true }
            };
            var message = NetworkFeedMessage.From(snapshot);
            message = JsonSerializer.Deserialize(JsonSerializer.Serialize(message, NetworkFeedJson.Default.NetworkFeedMessage), NetworkFeedJson.Default.NetworkFeedMessage)!;
            Check.Equal(1, message.Sockets.Count);
            Check.Equal(2, message.UdpPeers!.Count);
            using var client = new NetworkFeedClient();
            long ticks = Stopwatch.GetTimestamp();
            client.Accept(message, ticks);
            client.Accept(message with { UdpPeers = [] }, ticks + Stopwatch.Frequency);
            var received = UdpPeers.FromFeed([process], client.Current!);
            Check.Equal(100d, received.Sum(peer => peer.ReceiveBytesPerSecond));
            Check.Equal(100d, received.Sum(peer => peer.SendBytesPerSecond));
            Check.Equal(200d, client.Current!.Sockets.Single().ReceivePerSecond);
            Check.Equal(0, UdpPeers.FromFeed([UdpProcess(2)], client.Current!).Count);
            Check.Equal(0, UdpPeers.FromFeed([process], message with { UdpPeers = null }).Count);
        });
        yield return ("udp_peer_rows_replace_dual_stack_aggregate_once", "An aggregate attached to the IPv6 wildcard row is replaced by two IPv4 peers, with no duplicated rate or leftover wildcard row.", () =>
        {
            var peers = UdpPeers.FromFlows([UdpProcess()], [UdpFlow("203.0.113.7", 300, 100), UdpFlow("203.0.113.8", 100, 300)], 2);
            var shown = UdpPeers.ForDisplay([UdpSocket("::", 200, 200), UdpSocket("0.0.0.0", 0, 0)], peers);
            Check.Equal(2, shown.Count);
            Check.That(shown.All(peer => UdpPeers.KnownAddress(peer.RemoteAddress)), "wildcard aggregate retained");
            Check.Equal(200d, shown.Sum(peer => peer.ReceiveBytesPerSecond));
            Check.Equal(200d, shown.Sum(peer => peer.SendBytesPerSecond));
            Check.Equal("203.0.113.8", ConnectionOrdering.Sort(shown, "Upload", true).First().RemoteAddress);
            Check.Equal("203.0.113.7", ConnectionOrdering.Sort(shown, "Download", true).First().RemoteAddress);
        });
        yield return ("unknown_udp_traffic_is_not_lost_or_given_a_guessed_peer", "Partial peer data retains the unassigned remainder as unknown; an old service leaves the original socket row intact.", () =>
        {
            var socket = UdpSocket("::", 200, 200);
            var peers = UdpPeers.FromFlows([UdpProcess()], [UdpFlow("203.0.113.7", 100, 40)], 2);
            var shown = UdpPeers.ForDisplay([socket], peers);
            Check.Equal(2, shown.Count);
            Check.Equal(200d, shown.Sum(peer => peer.ReceiveBytesPerSecond));
            Check.Equal(200d, shown.Sum(peer => peer.SendBytesPerSecond));
            var unknown = shown.Single(peer => !UdpPeers.KnownAddress(peer.RemoteAddress));
            Check.Equal(150d, unknown.ReceiveBytesPerSecond);
            Check.Equal(180d, unknown.SendBytesPerSecond);
            Check.That(ReferenceEquals(socket, UdpPeers.ForDisplay([socket], []).Single()), "legacy socket changed");
        });
        yield return ("connection_sort_keeps_app_order", "Every connection sort and direction change leaves the app sort intact; app changes leave connections intact and both survive saving.", () =>
        {
            var settings = new AppSettings();
            var sort = new NetworkSortState(settings);
            sort.SelectApps("Data");
            foreach (string column in NetworkSortState.ConnectionColumns)
            {
                sort.SelectConnections(column);
                bool direction = sort.ConnectionsDescending;
                sort.SelectConnections(column, toggle: true);
                Check.Equal(!direction, sort.ConnectionsDescending);
                Check.Equal("Data", sort.AppColumn);
                Check.That(sort.AppsDescending, "connection sort reversed apps");
                sort.SelectApps("Speed");
                sort.ToggleApps();
                Check.Equal(column, sort.ConnectionColumn);
                Check.Equal(!direction, sort.ConnectionsDescending);
                sort.SelectApps("Data");
            }
            var loaded = new NetworkSortState(AppSettings.FromJson(JsonSerializer.Serialize(settings)));
            Check.Equal(sort.AppColumn, loaded.AppColumn);
            Check.Equal(sort.AppsDescending, loaded.AppsDescending);
            Check.Equal(sort.ConnectionColumn, loaded.ConnectionColumn);
            Check.Equal(sort.ConnectionsDescending, loaded.ConnectionsDescending);
        });
        yield return ("legacy_network_sort_migrates_without_blank_selector", "Old Remote/State choices move to connections; combined speed maps to the Download column, and invalid values get usable defaults.", () =>
        {
            foreach (string column in new[] { "Remote", "State", "Speed", "Name", "Data", "invalid" })
            {
                var settings = new AppSettings { NetworkSortColumn = column, NetworkSortDescending = true };
                var sort = new NetworkSortState(settings);
                bool connection = column is "Remote" or "State" or "Speed";
                Check.Equal(column == "Speed" ? "Download" : connection ? column : "Remote", sort.ConnectionColumn);
                Check.Equal(connection, sort.ConnectionsDescending);
                Check.That(NetworkSortState.AppColumns.Contains(sort.AppColumn), "app selector would be blank");
                Check.Equal(column is "Name" or "Data" or "Speed" ? column : "Name", sort.AppColumn);
            }
            var invalid = new NetworkSortState(new AppSettings { NetworkConnectionSortColumn = "invalid", NetworkConnectionSortDescending = true });
            Check.Equal("Remote", invalid.ConnectionColumn);
            Check.That(!invalid.ConnectionsDescending, "repaired sort direction wrong");
        });
        yield return ("connection_columns_sort_both_directions", "All displayed columns and each traffic direction order connections globally, including real app names and numeric local ports.", () =>
        {
            var rows = new[] { Connection(2, "z.example", 10, 30, "Listening", "WAN"), Connection(90, "a.example", 2, 10, "Established", "LAN"), Connection(1, "m.example", 3, 20, "Closing", "Local") };
            string App(ConnectionSample row) => row.Pid switch { 2 => "Alpha", 90 => "Zulu", _ => "Middle" };
            string Scope(ConnectionSample row) => row.Pid switch { 2 => "WAN", 90 => "LAN", _ => "Local" };
            var expected = new Dictionary<string, int[]>
            {
                ["App"] = [2, 1, 90], ["Local"] = [90, 1, 2], ["Scope"] = [90, 1, 2],
                ["Remote"] = [90, 1, 2], ["State"] = [1, 90, 2], ["Speed"] = [90, 1, 2],
                ["Download"] = [90, 1, 2], ["Upload"] = [90, 2, 1]
            };
            foreach (var (column, order) in expected)
            {
                Check.That(ConnectionOrdering.Sort(rows, column, false, App, Scope).Select(row => row.Pid).SequenceEqual(order), column + " ascending order wrong");
                Check.That(ConnectionOrdering.Sort(rows, column, true, App, Scope).Select(row => row.Pid).SequenceEqual(order.Reverse()), column + " descending order wrong");
            }
        });
        yield return ("filter_and_vpn_copies_are_not_added_to_hardware", "A physical NIC, three WFP/QoS copies and a VPN tunnel report the same traffic; count the NIC once in both directions.", () =>
        {
            AdapterTraffic[] Rows(ulong received, ulong sent) =>
            [
                Adapter("nic", received, sent), Adapter("wfp", received, sent, filter: true),
                Adapter("qos", received, sent, filter: true), Adapter("mac", received, sent, filter: true),
                Adapter("vpn", received, sent, hardware: false, name: "WireGuard Tunnel")
            ];
            var meter = new AdapterTrafficAccounting();
            Check.That(!meter.Sample(Rows(1000, 2000), 1).Sampled, "first reading must establish a baseline");
            var result = meter.Sample(Rows(1100, 2040), 2);
            Check.Equal(50d, result.Receive);
            Check.Equal(20d, result.Send);
            Check.That(result.Sampled, "valid hardware interval missing");
        });
        yield return ("multiple_hardware_nics_are_aggregated", "Two real NICs carrying different traffic both contribute; disconnected and loopback interfaces do not.", () =>
        {
            var meter = new AdapterTrafficAccounting();
            meter.Sample([Adapter("a", 100, 100), Adapter("b", 100, 100)], 1);
            var result = meter.Sample([Adapter("a", 140, 120), Adapter("b", 180, 150), Adapter("off", 90000, 90000, up: false), Adapter("loop", 90000, 90000, type: 24)], 2);
            Check.Equal(60d, result.Receive);
            Check.Equal(35d, result.Send);
        });
        yield return ("virtual_machine_uses_non_tunnel_interface", "A machine with no active hardware NIC uses its guest Ethernet adapter, excluding filters, tunnels and VPN Ethernet adapters.", () =>
        {
            var selected = AdapterTrafficAccounting.Select([
                Adapter("guest", hardware: false, name: "Guest Ethernet"), Adapter("off", up: false),
                Adapter("filter", hardware: false, filter: true), Adapter("tunnel", hardware: false, type: 131),
                Adapter("encapsulated", hardware: false, tunnelType: 13), Adapter("vpn", hardware: false, name: "WireGuard Tunnel")]);
            Check.That(selected.Select(row => row.Id).SequenceEqual(new[] { "guest" }), "guest network missing or duplicate layer retained");
        });
        yield return ("adapter_changes_and_counter_resets_do_not_spike", "New/reconnected NICs start a baseline; counter resets and invalid durations never turn cumulative bytes into a transfer rate.", () =>
        {
            var meter = new AdapterTrafficAccounting();
            meter.Sample([Adapter("a", 100, 100)], 1);
            var result = meter.Sample([Adapter("a", 120, 130), Adapter("b", ulong.MaxValue - 10, 500000)], 1);
            Check.Equal(20d, result.Receive);
            Check.Equal(30d, result.Send);
            Check.That(!meter.Sample([Adapter("a", 1, 1)], 1).Sampled, "counter reset counted");
            Check.That(!meter.Sample([], 1).Sampled, "empty capture reported as measured");
            Check.That(!meter.Sample([Adapter("a", 900000, 900000)], 1).Sampled, "reconnect counted lifetime bytes");
            Check.That(!meter.Sample([Adapter("a", 900001, 900001)], double.NaN).Sampled, "invalid elapsed time accepted");
            Check.That(!meter.Sample([Adapter("a", 900002, 900002)], 0).Sampled, "zero elapsed time accepted");
            result = meter.Sample([Adapter("a", 900012, 900022)], 2);
            Check.Equal(5d, result.Receive);
            Check.Equal(10d, result.Send);
        });
        yield return ("adapter_layer_switch_starts_new_baseline", "Switching between hardware and a VM-only fallback does not combine deltas from different layers.", () =>
        {
            var meter = new AdapterTrafficAccounting();
            meter.Sample([Adapter("guest", 10, 10, hardware: false)], 1);
            Check.That(!meter.Sample([Adapter("guest", 50, 50, hardware: false), Adapter("nic", 50000, 50000)], 1).Sampled, "layer transition counted");
            Check.That(!meter.Sample([Adapter("guest", 100, 100, hardware: false)], 1).Sampled, "fallback transition counted");
            Check.Equal(10d, meter.Sample([Adapter("guest", 120, 140, hardware: false)], 2).Receive);
        });
        yield return ("native_interface_layout_matches_windows_abi", "Native interface row stride and hardware/filter/counter offsets match MIB_IF_ROW2 on supported 64-bit builds.", () =>
        {
            Check.Equal(1352, Marshal.SizeOf<AdapterTrafficReader.InterfaceRow>());
            Check.Equal(1152L, Marshal.OffsetOf<AdapterTrafficReader.InterfaceRow>(nameof(AdapterTrafficReader.InterfaceRow.Flags)).ToInt64());
            Check.Equal(1208L, Marshal.OffsetOf<AdapterTrafficReader.InterfaceRow>(nameof(AdapterTrafficReader.InterfaceRow.InOctets)).ToInt64());
            Check.Equal(1280L, Marshal.OffsetOf<AdapterTrafficReader.InterfaceRow>(nameof(AdapterTrafficReader.InterfaceRow.OutOctets)).ToInt64());
        });
    }

    private static AdapterTraffic Adapter(string id, ulong received = 0, ulong sent = 0, bool hardware = true,
        bool filter = false, bool up = true, uint type = 6, uint tunnelType = 0, string? name = null) =>
        new(id, name ?? id, hardware, filter, up, type, tunnelType, received, sent);

    private static ProcessSample UdpProcess(long created = 1) => new() { Pid = 42, ParentPid = 0, CreateTime = created,
        ImageName = "Test.exe", Path = @"C:\Test\Test.exe", Description = "Test", Company = "Test" };
    private static FlowSample UdpFlow(string remote, long received, long sent) => new() { Pid = 42, Protocol = "UDP",
        LocalAddress = "192.0.2.10", LocalPort = 50000, RemoteAddress = remote, RemotePort = 443, Received = received, Sent = sent };
    private static ConnectionSample UdpSocket(string local, double down, double up) => new() { Pid = 42, Protocol = "UDP",
        LocalAddress = local, LocalPort = 50000, RemoteAddress = "", RemotePort = 0, State = "Listening",
        ReceiveBytesPerSecond = down, SendBytesPerSecond = up };

    private static ConnectionSample Connection(int pid, string host, int localPort, double speed, string state, string scope) => new()
    {
        Pid = pid, Protocol = "TCP", LocalAddress = "192.0.2.1", LocalPort = localPort,
        RemoteAddress = host, RemotePort = 443, RemoteHost = host, State = state, ReceiveBytesPerSecond = speed,
        SendBytesPerSecond = pid switch { 2 => 5, 90 => 3, _ => 7 }
    };
}
