using System.Globalization;
using BetterTaskManager.Core.Monitoring;
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
    string State,
    string Summary,
    string Transferred,
    string Speed,
    string RemoteDetail,
    bool Expandable,
    bool Expanded);

public sealed class NetworkSlot : ObservableObject
{
    /// <summary>What UI Automation and screen readers announce for the row.</summary>
    public override string ToString() => Name;

    private static readonly Thickness ChildIndent = new(40, 0, 0, 0);
    private string name = "", detail = "", local = "", remote = "", state = "", path = "", summary = "", transferred = "", speed = "", remoteDetail = "";
    private ImageSource? icon;
    private Visibility chevronVisibility, iconVisibility, blockedVisibility = Visibility.Collapsed;
    private double chevronAngle;
    private Thickness indent;
    private bool isGroup;

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;
    public NetworkRowData? Data { get; private set; }
    public string Key => Data?.Key ?? "";

    public string Name { get => name; private set => Set(ref name, value); }
    public string Detail { get => detail; private set => Set(ref detail, value); }
    public string Local { get => local; private set => Set(ref local, value); }
    public string Remote { get => remote; private set => Set(ref remote, value); }
    public string State { get => state; private set => Set(ref state, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string Transferred { get => transferred; private set => Set(ref transferred, value); }
    public string Speed { get => speed; private set => Set(ref speed, value); }
    /// <summary>Numeric endpoint and name source, shown as the remote column's tooltip.</summary>
    public string RemoteDetail { get => remoteDetail; private set => Set(ref remoteDetail, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public Visibility ChevronVisibility { get => chevronVisibility; private set => Set(ref chevronVisibility, value); }
    public Visibility IconVisibility { get => iconVisibility; private set => Set(ref iconVisibility, value); }
    public Visibility BlockedVisibility { get => blockedVisibility; private set => Set(ref blockedVisibility, value); }
    public double ChevronAngle { get => chevronAngle; private set => Set(ref chevronAngle, value); }
    public Thickness Indent { get => indent; private set => Set(ref indent, value); }
    public Windows.UI.Text.FontWeight NameWeight => isGroup ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public static void Load(NetworkSlot slot, (NetworkRowData Row, Func<string, bool> IsBlocked) input)
    {
        NetworkRowData row = input.Row;
        slot.Data = row;
        bool group = row.Kind == RowKind.Group;
        slot.Name = row.Name;
        slot.Detail = row.Detail;
        slot.Local = row.Local;
        slot.Remote = row.Remote;
        slot.State = row.State;
        slot.Path = row.Path;
        slot.Summary = row.Summary;
        slot.Transferred = row.Transferred;
        slot.Speed = row.Speed;
        slot.RemoteDetail = row.RemoteDetail;
        slot.Indent = group ? default : ChildIndent;
        slot.IconVisibility = group ? Visibility.Visible : Visibility.Collapsed;
        slot.Icon = group ? IconCache.Get(row.Path) : null;
        slot.ChevronVisibility = row.Expandable ? Visibility.Visible : Visibility.Collapsed;
        slot.ChevronAngle = row.Expanded ? 90 : 0;
        slot.BlockedVisibility = group && input.IsBlocked(row.Path) ? Visibility.Visible : Visibility.Collapsed;
        if (slot.isGroup != group)
        {
            slot.isGroup = group;
            slot.Raise(nameof(NameWeight));
        }
    }
}

public sealed class NetworkViewModel : ObservableObject
{
    public const string SortName = "Name";
    public const string SortRemote = "Remote";
    public const string SortState = "State";
    public const string SortData = "Data";
    public const string SortSpeed = "Speed";

    private readonly MonitorHost monitor;
    private readonly AppSettings settings;
    private readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);

    public NetworkViewModel(MonitorHost monitor, AppSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        if (settings.NetworkSortColumn is not (SortName or SortRemote or SortState or SortData or SortSpeed)) settings.NetworkSortColumn = SortName;
        Layout = new ColumnLayout("Network.", new Dictionary<string, double>
        {
            ["Name"] = 320, ["Local"] = 240, ["Remote"] = 280, ["State"] = 110, ["Data"] = 170, ["Speed"] = 190
        }, settings.ColumnWidths);
        NetworkSlot.SharedLayout = Layout;
        Rows = new SlotCollection<NetworkSlot, (NetworkRowData, Func<string, bool>)>(NetworkSlot.Load);
    }

    public ColumnLayout Layout { get; }
    public SlotCollection<NetworkSlot, (NetworkRowData, Func<string, bool>)> Rows { get; }
    public bool EstablishedOnly { get; set; }
    public string Summary { get; private set; } = "";
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
                    ? (first.Pid == 0 ? "Closing connections (no owning process)" : "PID " + first.Pid)
                    : owner.ImageName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase) && owner.Services is { Count: > 0 } services
                        ? "Service Host: " + services[0]
                        : owner.DisplayName;
                return (Key: group.Key, Name: name, Path: owner?.Path ?? "", Connections: group.ToList());
            })
            .Where(group => query.Length == 0 || group.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                group.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                group.Connections.Any(connection => Endpoint(connection.RemoteAddress, connection.RemotePort).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (connection.RemoteHost?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    connection.Pid.ToString(CultureInfo.InvariantCulture) == query))
            .ToList();
        groups = SortGroups(groups, traffic);

        var rows = new List<NetworkRowData>();
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            int established = group.Connections.Count(connection => connection.State == "Established");
            int listening = group.Connections.Count(connection => connection.State == "Listening");
            bool isExpanded = expanded.Contains(group.Key) || (query.Length > 0 && group.Connections.Count <= 50);
            traffic.TryGetValue(group.Key, out var usage);
            string summary = $"{established} established · {listening} listening · {group.Connections.Count - established - listening} other";
            string data = measured && usage.Received + usage.Sent > 0 ? $"↓ {Format.Bytes(usage.Received)}   ↑ {Format.Bytes(usage.Sent)}" : "";
            string speed = measured ? Speed(usage.Down, usage.Up) : "";
            rows.Add(new NetworkRowData(RowKind.Group, group.Key, group.Name, $"({group.Connections.Count})", group.Path, "", "", "", summary, data, speed, "", true, isExpanded));
            if (!isExpanded) continue;

            foreach (ConnectionSample connection in SortConnections(group.Connections))
            {
                // Several sockets can share one endpoint (e.g. mDNS on UDP 5353); keep row keys unique.
                string key = $"{group.Key}|{connection.Pid}|{connection.Protocol}|{connection.LocalAddress}|{connection.LocalPort}|{connection.RemoteAddress}|{connection.RemotePort}";
                int occurrence = keyCounts[key] = keyCounts.GetValueOrDefault(key) + 1;
                rows.Add(new NetworkRowData(RowKind.Child,
                    occurrence == 1 ? key : key + "#" + occurrence,
                    connection.Protocol, "PID " + connection.Pid, group.Path,
                    Endpoint(connection.LocalAddress, connection.LocalPort),
                    connection.Protocol == "UDP" ? "*"
                        : connection.RemoteHost is { } host ? $"{host}:{connection.RemotePort}" : Endpoint(connection.RemoteAddress, connection.RemotePort),
                    connection.State, "", "",
                    measured && connection.ReceiveBytesPerSecond + connection.SendBytesPerSecond > 0
                        ? Speed(connection.ReceiveBytesPerSecond, connection.SendBytesPerSecond) : "",
                    RemoteDetail(connection),
                    false, false));
            }
        }

        int totalEstablished = snapshot.Connections.Count(connection => connection.State == "Established");
        string throughput = measured
            ? $" · all apps ↓ {Format.NetworkRate(snapshot.Processes.Sum(p => p.NetworkReceiveBytesPerSecond))} ↑ {Format.NetworkRate(snapshot.Processes.Sum(p => p.NetworkSendBytesPerSecond))}"
            : " · per-app speed needs administrator rights";
        Summary = $"{Format.Count(snapshot.Connections.Count)} connections · {Format.Count(totalEstablished)} established · " +
            $"{groups.Count} apps shown" + throughput + (snapshot.NetworkIssues.Count > 0 ? " · some network tables could not be read" : "");
        Func<string, bool> isBlocked = monitor.IsBlocked;
        Rows.Apply(rows.Select(row => (row, isBlocked)).ToList());
    }

    private List<(string Key, string Name, string Path, List<ConnectionSample> Connections)> SortGroups(
        List<(string Key, string Name, string Path, List<ConnectionSample> Connections)> groups,
        Dictionary<string, (double Down, double Up, long Received, long Sent)> traffic)
    {
        Func<(string Key, string Name, string Path, List<ConnectionSample> Connections), double>? measure = SortColumn switch
        {
            SortState => group => group.Connections.Count(connection => connection.State == "Established") * 100000d + group.Connections.Count,
            SortData => group => traffic.TryGetValue(group.Key, out var usage) ? usage.Received + usage.Sent : 0,
            SortSpeed => group => traffic.TryGetValue(group.Key, out var usage) ? usage.Down + usage.Up : 0,
            _ => null
        };
        var byName = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<(string Key, string Name, string Path, List<ConnectionSample> Connections)> ordered = measure is null
            ? (SortDescending ? groups.OrderByDescending(group => group.Name, byName) : groups.OrderBy(group => group.Name, byName))
            : (SortDescending ? groups.OrderByDescending(measure) : groups.OrderBy(measure)).ThenBy(group => group.Name, byName);
        return ordered.ThenBy(group => group.Key, StringComparer.Ordinal).ToList();
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

    public void ToggleExpanded(string key)
    {
        if (!expanded.Remove(key)) expanded.Add(key);
        Refresh();
    }

    private static string RemoteDetail(ConnectionSample connection)
    {
        if (connection.Protocol == "UDP") return "UDP sockets have no fixed remote side";
        string endpoint = Endpoint(connection.RemoteAddress, connection.RemotePort);
        if (connection.RemoteHost is null) return endpoint;
        return connection.RemoteHostIsReverse
            ? $"{endpoint}\nName from reverse DNS; may be the hosting provider rather than the service"
            : $"{endpoint}\nName the app looked up";
    }

    /// <summary>Blank when idle (both directions under 0.5 kbit/s, which would print as 0) so active apps stand out.</summary>
    private static string Speed(double down, double up) =>
        down * 8 < 500 && up * 8 < 500 ? "" : $"↓ {Format.NetworkRate(down)}   ↑ {Format.NetworkRate(up)}";

    private static string Endpoint(string address, int port) =>
        address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
}
