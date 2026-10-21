using System.Globalization;
using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Network;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

public sealed record NetworkRowData(
    RowKind Kind,
    string Key,
    string Name,
    string Detail,
    string Path,
    string Local,
    string Remote,
    string Scope,
    string State,
    string Summary,
    string Transferred,
    string Speed,
    string RemoteDetail,
    bool Expandable,
    bool Tunnel,
    bool Expanded,
    double Down = 0,
    double Up = 0,
    long Received = 0,
    long Sent = 0,
    int Established = 0,
    int Listening = 0,
    int Pid = 0,
    string RemoteAddress = "",
    string RawState = "",
    string AppName = "",
    int Count = 0);

public sealed class NetworkSlot : RowSlot
{
    /// <summary>What UI Automation and screen readers announce for the row.</summary>
    public override string ToString() => Name;

    private string name = "", detail = "", local = "", remote = "", state = "", path = "", summary = "", transferred = "", scope = "", speed = "", remoteDetail = "", appName = "", subline = "";
    private ImageSource? icon;
    private Visibility iconVisibility, blockedVisibility = Visibility.Collapsed, rulesVisibility = Visibility.Collapsed, tunnelVisibility = Visibility.Collapsed;
    private Brush stateBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private bool isGroup;

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;
    /// <summary>The busiest app's rates, so every app's meters are drawn relative to it.</summary>
    public static double DownScale { get; set; } = 1;
    public static double UpScale { get; set; } = 1;

    public NetworkRowData? Data { get; private set; }
    public string Key => Data?.Key ?? "";

    public string Name { get => name; private set => Set(ref name, value); }
    public string Detail { get => detail; private set => Set(ref detail, value); }
    public string Local { get => local; private set => Set(ref local, value); }
    public string Remote { get => remote; private set => Set(ref remote, value); }
    public string State { get => state; private set => Set(ref state, value); }
    /// <summary>A colored dot before the state: green for an open connection, gray for a listener, amber in between.</summary>
    public Brush StateBrush { get => stateBrush; private set => Set(ref stateBrush, value); }
    public Visibility StateDotVisibility => state.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Scope { get => scope; private set => Set(ref scope, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string Transferred { get => transferred; private set => Set(ref transferred, value); }
    public string Speed { get => speed; private set => Set(ref speed, value); }
    /// <summary>Numeric endpoint and name source, shown as the remote column's tooltip.</summary>
    public string RemoteDetail { get => remoteDetail; private set => Set(ref remoteDetail, value); }
    /// <summary>The app a connection belongs to, for the all-apps view.</summary>
    public string AppName { get => appName; private set => Set(ref appName, value); }
    /// <summary>Second line of an app row: connection counts and the current rates.</summary>
    public string Subline { get => subline; private set => Set(ref subline, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public Visibility IconVisibility { get => iconVisibility; private set => Set(ref iconVisibility, value); }
    public Visibility BlockedVisibility { get => blockedVisibility; private set => Set(ref blockedVisibility, value); }
    public Visibility RulesVisibility { get => rulesVisibility; private set => Set(ref rulesVisibility, value); }
    public Visibility TunnelVisibility { get => tunnelVisibility; private set => Set(ref tunnelVisibility, value); }
    public Meter DownMeter { get; } = new();
    public Meter UpMeter { get; } = new();
    public Windows.UI.Text.FontWeight NameWeight => isGroup ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    private static readonly Dictionary<string, Brush> stateBrushes = new();

    private static Brush StateBrushFor(string state)
    {
        string key = state == Loc.State("Established") ? "StateGoodBrush"
            : state == Loc.State("Listening") || state.Length == 0 ? "StateIdleBrush"
            : "StateWarnBrush";
        if (!stateBrushes.TryGetValue(key, out Brush? brush))
        {
            brush = (Brush)Application.Current.Resources[key];
            stateBrushes[key] = brush;
        }
        return brush;
    }

    public static void Load(NetworkSlot slot, (NetworkRowData Row, Func<string, NetworkAccess> Access) input)
    {
        NetworkRowData row = input.Row;
        slot.Data = row;
        bool group = row.Kind == RowKind.Group;
        slot.Name = row.Name;
        slot.Detail = row.Detail;
        slot.Local = row.Local;
        slot.Remote = row.Remote;
        slot.State = row.State;
        slot.StateBrush = StateBrushFor(row.State);
        slot.Raise(nameof(StateDotVisibility));
        slot.Scope = row.Scope;
        slot.Path = row.Path;
        slot.Summary = row.Summary;
        slot.Transferred = row.Transferred;
        slot.Speed = row.Speed;
        slot.RemoteDetail = row.RemoteDetail;
        slot.AppName = row.AppName;
        slot.Subline = group ? Loc.F("Network_AppSubline", row.Established, row.Count) + (row.Speed.Length > 0 ? " · " + row.Speed.Replace("   ", " ") : "") : "";
        slot.IconVisibility = group && row.Path.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        slot.Icon = group ? IconCache.Get(row.Path) : null;
        slot.DownMeter.Set(row.Down, DownScale);
        slot.UpMeter.Set(row.Up, UpScale);
        NetworkAccess access = group && row.Path.Length > 0 ? input.Access(row.Path) : NetworkAccess.Open;
        slot.BlockedVisibility = access == NetworkAccess.Blocked ? Visibility.Visible : Visibility.Collapsed;
        slot.RulesVisibility = access == NetworkAccess.Rules ? Visibility.Visible : Visibility.Collapsed;
        slot.TunnelVisibility = row.Tunnel ? Visibility.Visible : Visibility.Collapsed;
        if (slot.isGroup != group)
        {
            slot.isGroup = group;
            slot.Raise(nameof(NameWeight));
        }
    }
}

/// <summary>
/// The Network page as master-detail: the apps with open sockets on the left (plus an "All apps" row), the
/// connections of the selected app in the table. Sorting by name, data or speed orders the apps; sorting by remote
/// host, state or speed orders the connections.
/// </summary>
public sealed class NetworkViewModel : ObservableObject
{
    public const string SortName = "Name";
    public const string SortRemote = "Remote";
    public const string SortState = "State";
    public const string SortData = "Data";
    public const string SortSpeed = "Speed";
    public const string AllAppsKey = "*";

    private readonly MonitorHost monitor;
    private readonly AppSettings settings;

    public NetworkViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        if (settings.NetworkSortColumn is not (SortName or SortRemote or SortState or SortData or SortSpeed)) settings.NetworkSortColumn = SortName;
        Layout = new ColumnLayout("Connections.", new Dictionary<string, double>
        {
            ["Remote"] = 300, ["App"] = 180, ["Local"] = 200, ["Scope"] = 110, ["State"] = 130, ["Speed"] = 170
        }, settings.ColumnWidths, ["App", "Local", "Scope", "State", "Speed"], settings.ColumnOrder);
        NetworkSlot.SharedLayout = Layout;
        Apps = new SlotCollection<NetworkSlot, (NetworkRowData, Func<string, NetworkAccess>)>(NetworkSlot.Load);
        Connections = new SlotCollection<NetworkSlot, (NetworkRowData, Func<string, NetworkAccess>)>(NetworkSlot.Load);
    }

    public ColumnLayout Layout { get; }
    public SlotCollection<NetworkSlot, (NetworkRowData, Func<string, NetworkAccess>)> Apps { get; }
    public SlotCollection<NetworkSlot, (NetworkRowData, Func<string, NetworkAccess>)> Connections { get; }
    public bool EstablishedOnly { get; set; }
    /// <summary>The app whose connections fill the table; <see cref="AllAppsKey"/> shows every connection.</summary>
    public string SelectedAppKey { get; set; } = AllAppsKey;
    public string Summary { get; private set; } = "";
    /// <summary>Apps shown after the search filter, for the connections tile.</summary>
    public int AppCount { get; private set; }
    /// <summary>The selected app's current rates (or everything's), for the traffic chart.</summary>
    public double SelectedDown { get; private set; }
    public double SelectedUp { get; private set; }
    public string SortColumn => settings.NetworkSortColumn;
    public bool SortDescending => settings.NetworkSortDescending;

    /// <summary>Clicking the active column flips the direction; a new column starts with its natural direction.</summary>
    public void Sort(string column)
    {
        if (settings.NetworkSortColumn == column)
        {
            settings.NetworkSortDescending = !settings.NetworkSortDescending;
        }
        else
        {
            settings.NetworkSortColumn = column;
            settings.NetworkSortDescending = column is SortState or SortData or SortSpeed;
        }
        Raise(nameof(SortColumn));
        Refresh();
    }

    /// <summary>Sets the app list's order without flipping; used by the "Sort by" dropdown.</summary>
    public void SortApps(string column)
    {
        settings.NetworkSortColumn = column;
        settings.NetworkSortDescending = column is SortData or SortSpeed;
        Raise(nameof(SortColumn));
        Refresh();
    }

    public void Refresh()
    {
        if (monitor.Latest is not { } snapshot) return;
        var processes = snapshot.Processes.ToDictionary(process => process.Pid);
        string query = monitor.SearchText.Trim();
        bool measured = snapshot.System.PerProcessNetworkAvailable;

        // Throughput and totals per app group, from every process in the group (not only those with open sockets).
        var traffic = new Dictionary<string, (double Down, double Up, long Received, long Sent)>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessSample process in snapshot.Processes)
        {
            string key = ProcessTree.GroupKey(process);
            traffic.TryGetValue(key, out var sum);
            traffic[key] = (sum.Down + process.NetworkReceiveBytesPerSecond, sum.Up + process.NetworkSendBytesPerSecond,
                sum.Received + process.NetworkReceivedTotal, sum.Sent + process.NetworkSentTotal);
        }

        var groups = snapshot.Connections
            .Where(connection => !EstablishedOnly || (connection.Protocol == "TCP" && connection.State is not ("Listening" or "Time Wait")))
            .GroupBy(connection => processes.TryGetValue(connection.Pid, out ProcessSample? process) ? ProcessTree.GroupKey(process) : "pid:" + connection.Pid)
            .Select(group =>
            {
                ConnectionSample first = group.First();
                processes.TryGetValue(first.Pid, out ProcessSample? owner);
                string name = owner is null || first.Pid == 0
                    ? (first.Pid == 0 ? Loc.Get("Network_Closing") : "PID " + first.Pid)
                    : Loc.AppName(AppIdentityRules.AppName(owner));
                return (Key: group.Key, Name: name, Path: owner?.Path ?? "", Connections: group.ToList(), Tunnel: owner is not null && VpnTunnels.IsTunnel(owner));
            })
            .Where(group => query.Length == 0 || group.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                group.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                group.Connections.Any(connection => Endpoint(connection.RemoteAddress, connection.RemotePort).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (connection.RemoteHost?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    connection.Pid.ToString(CultureInfo.InvariantCulture) == query))
            .ToList();
        groups = SortGroups(groups, traffic);

        // App rows: "All apps" first, then every app with its counts and rates; meters relative to the busiest app.
        double allDown = 0, allUp = 0;
        long allReceived = 0, allSent = 0;
        int allEstablished = 0, allListening = 0, allCount = 0;
        var appRows = new List<NetworkRowData>();
        foreach (var group in groups)
        {
            int established = group.Connections.Count(connection => connection.State == "Established");
            int listening = group.Connections.Count(connection => connection.State == "Listening");
            traffic.TryGetValue(group.Key, out var usage);
            string summary = Loc.F("Network_GroupSummary", established, listening, group.Connections.Count - established - listening);
            string data = measured && usage.Received + usage.Sent > 0 ? $"↓ {Format.Bytes(usage.Received)}   ↑ {Format.Bytes(usage.Sent)}" : "";
            string speed = measured ? Speed(usage.Down, usage.Up) : "";
            appRows.Add(new NetworkRowData(RowKind.Group, group.Key, group.Name, $"({group.Connections.Count})", group.Path, "", "", "", "", summary, data, speed, "", false, group.Tunnel, false,
                usage.Down, usage.Up, usage.Received, usage.Sent, established, listening, Count: group.Connections.Count));
            allDown += usage.Down;
            allUp += usage.Up;
            allReceived += usage.Received;
            allSent += usage.Sent;
            allEstablished += established;
            allListening += listening;
            allCount += group.Connections.Count;
        }
        NetworkSlot.DownScale = Math.Max(appRows.Count == 0 ? 0 : appRows.Max(row => row.Down), 1024);
        NetworkSlot.UpScale = Math.Max(appRows.Count == 0 ? 0 : appRows.Max(row => row.Up), 1024);
        appRows.Insert(0, new NetworkRowData(RowKind.Group, AllAppsKey, Loc.Get("Network_AllAppsRow"), $"({allCount})", "", "", "", "", "",
            Loc.F("Network_GroupSummary", allEstablished, allListening, allCount - allEstablished - allListening),
            measured && allReceived + allSent > 0 ? $"↓ {Format.Bytes(allReceived)}   ↑ {Format.Bytes(allSent)}" : "",
            measured ? Speed(allDown, allUp) : "", "", false, false, false,
            allDown, allUp, allReceived, allSent, allEstablished, allListening, Count: allCount));

        // Connection rows of the selected app, or of every app.
        var connectionRows = new List<NetworkRowData>();
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        // An app whose sockets are all closed for a moment keeps the selection; its table is just empty until then.
        bool all = SelectedAppKey == AllAppsKey;
        foreach (var group in groups)
        {
            if (!all && group.Key != SelectedAppKey) continue;
            foreach (ConnectionSample connection in SortConnections(group.Connections))
            {
                // Several sockets can share one endpoint (e.g. mDNS on UDP 5353); keep row keys unique.
                string key = $"{group.Key}|{connection.Pid}|{connection.Protocol}|{connection.LocalAddress}|{connection.LocalPort}|{connection.RemoteAddress}|{connection.RemotePort}";
                int occurrence = keyCounts[key] = keyCounts.GetValueOrDefault(key) + 1;
                connectionRows.Add(new NetworkRowData(RowKind.Child,
                    occurrence == 1 ? key : key + "#" + occurrence,
                    connection.Protocol, "PID " + connection.Pid, group.Path,
                    Endpoint(connection.LocalAddress, connection.LocalPort),
                    connection.Protocol == "UDP" ? "*"
                        : connection.RemoteHost is { } host ? $"{(connection.RemoteHostFromOtherApp ? "≈ " : "")}{host}:{connection.RemotePort}" : Endpoint(connection.RemoteAddress, connection.RemotePort),
                    ScopeText(connection),
                    Loc.State(connection.State), "", "",
                    measured && connection.ReceiveBytesPerSecond + connection.SendBytesPerSecond > 0
                        ? Speed(connection.ReceiveBytesPerSecond, connection.SendBytesPerSecond) : "",
                    RemoteDetail(connection),
                    false, false, false,
                    connection.ReceiveBytesPerSecond, connection.SendBytesPerSecond, 0, 0, 0, 0, connection.Pid,
                    connection.Protocol == "UDP" ? "" : Endpoint(connection.RemoteAddress, connection.RemotePort), connection.State, group.Name));
            }
        }
        if (all) (SelectedDown, SelectedUp) = (allDown, allUp);
        else
        {
            traffic.TryGetValue(SelectedAppKey, out var selectedUsage);
            (SelectedDown, SelectedUp) = (selectedUsage.Down, selectedUsage.Up);
        }

        int totalEstablished = snapshot.Connections.Count(connection => connection.State == "Established");
        List<ProcessSample> counted = snapshot.Processes.Where(process => !VpnTunnels.IsTunnel(process)).ToList();
        string throughput = measured
            ? Loc.F("Network_AllApps", Format.NetworkRate(counted.Sum(p => p.NetworkReceiveBytesPerSecond)), Format.NetworkRate(counted.Sum(p => p.NetworkSendBytesPerSecond))) +
              (counted.Count < snapshot.Processes.Count ? Loc.Get("Network_VpnNotCounted") : "")
            : Loc.Get("Network_NeedsAdmin");
        AppCount = groups.Count;
        Summary = Loc.F("Network_Summary", Format.Count(snapshot.Connections.Count), Format.Count(totalEstablished), groups.Count) +
            throughput + (snapshot.System.PerProcessNetworkFromService ? Loc.Get("Network_FromService") : "") + (snapshot.NetworkIssues.Count > 0 ? Loc.Get("Network_TablesFailed") : "");
        Func<string, NetworkAccess> access = monitor.AccessOf;
        Apps.Apply(appRows.Select(row => (row, access)).ToList());
        Connections.Apply(connectionRows.Select(row => (row, access)).ToList());
    }

    private List<(string Key, string Name, string Path, List<ConnectionSample> Connections, bool Tunnel)> SortGroups(
        List<(string Key, string Name, string Path, List<ConnectionSample> Connections, bool Tunnel)> groups,
        Dictionary<string, (double Down, double Up, long Received, long Sent)> traffic)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<(string Key, string Name, string Path, List<ConnectionSample> Connections, bool Tunnel)> ordered;

        if (SortColumn == SortRemote)
        {
            // Order apps by their alphabetically first remote host, so clicking the header visibly reorders the list;
            // the connections inside each app are ordered by host too (see SortConnections).
            var byHost = StringComparer.OrdinalIgnoreCase;
            ordered = (SortDescending
                ? groups.OrderByDescending(group => GroupHost(group.Connections), byHost)
                : groups.OrderBy(group => GroupHost(group.Connections), byHost)).ThenBy(group => group.Name, byName);
        }
        else
        {
            Func<(string Key, string Name, string Path, List<ConnectionSample> Connections, bool Tunnel), double>? measure = SortColumn switch
            {
                SortState => group => group.Connections.Count(connection => connection.State == "Established") * 100000d + group.Connections.Count,
                SortData => group => traffic.TryGetValue(group.Key, out var usage) ? usage.Received + usage.Sent : 0,
                SortSpeed => group => traffic.TryGetValue(group.Key, out var usage) ? usage.Down + usage.Up : 0,
                _ => null
            };
            ordered = measure is null
                ? (SortDescending ? groups.OrderByDescending(group => group.Name, byName) : groups.OrderBy(group => group.Name, byName))
                : (SortDescending ? groups.OrderByDescending(measure) : groups.OrderBy(measure)).ThenBy(group => group.Name, byName);
        }
        return ordered.ThenBy(group => group.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>An app's representative remote host for sorting: the alphabetically first host it talks to. Apps with
    /// no remote host (only listeners or UDP sockets) sort last.</summary>
    private static string GroupHost(List<ConnectionSample> connections)
    {
        string? best = null;
        foreach (ConnectionSample connection in connections)
        {
            if (connection.Protocol != "TCP" || connection.State == "Listening") continue;
            string host = connection.RemoteHost ?? connection.RemoteAddress;
            if (host.Length == 0) continue;
            if (best is null || string.Compare(host, best, StringComparison.OrdinalIgnoreCase) < 0) best = host;
        }
        return best ?? "￿";
    }

    /// <summary>
    /// Connections keep a fixed order (state, host, ports) unless the page is sorted by host or speed, so a group's
    /// rows do not reshuffle every refresh.
    /// </summary>
    private IEnumerable<ConnectionSample> SortConnections(List<ConnectionSample> connections)
    {
        static int StateRank(ConnectionSample connection) => connection.State == "Established" ? 0 : connection.State == "Listening" ? 2 : 1;
        static string Host(ConnectionSample connection) => connection.RemoteHost ?? connection.RemoteAddress;
        IOrderedEnumerable<ConnectionSample> ordered = SortColumn switch
        {
            SortSpeed => SortDescending
                ? connections.OrderByDescending(connection => connection.ReceiveBytesPerSecond + connection.SendBytesPerSecond)
                : connections.OrderBy(connection => connection.ReceiveBytesPerSecond + connection.SendBytesPerSecond),
            SortRemote => SortDescending
                ? connections.OrderByDescending(Host, StringComparer.OrdinalIgnoreCase)
                : connections.OrderBy(Host, StringComparer.OrdinalIgnoreCase),
            _ => connections.OrderBy(StateRank)
        };
        return ordered
            .ThenBy(StateRank)
            .ThenBy(Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.RemotePort)
            .ThenBy(connection => connection.Protocol, StringComparer.Ordinal)
            .ThenBy(connection => connection.LocalPort);
    }

    /// <summary>"Internet", "LAN · in"…; blank for listeners and UDP sockets, which have no remote side.</summary>
    private static string ScopeText(ConnectionSample connection)
    {
        string scope = Loc.Scope(IpScopes.Label(connection.Scope));
        if (scope.Length == 0) return "";
        return connection.Inbound ? scope + Loc.Get("Scope_Inbound") : scope;
    }

    private static string RemoteDetail(ConnectionSample connection)
    {
        if (connection.Protocol == "UDP") return Loc.Get("Remote_UdpNone");
        string endpoint = Endpoint(connection.RemoteAddress, connection.RemotePort);
        if (connection.RemoteHost is null) return endpoint;
        return connection.RemoteHostIsReverse ? Loc.F("Remote_Reverse", endpoint)
            : connection.RemoteHostFromOtherApp ? Loc.F("Remote_OtherApp", endpoint)
            : Loc.F("Remote_Lookup", endpoint);
    }

    /// <summary>Blank when idle (both directions under 0.5 kbit/s, which would print as 0) so active apps stand out.</summary>
    private static string Speed(double down, double up) =>
        down * 8 < 500 && up * 8 < 500 ? "" : $"↓ {Format.NetworkRate(down)}   ↑ {Format.NetworkRate(up)}";

    private static string Endpoint(string address, int port) =>
        address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
}
