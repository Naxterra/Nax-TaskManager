using BetterTaskManager.Core.Monitoring;

namespace BetterTaskManager.Fluent.ViewModels;

public enum RowKind
{
    Section,
    Group,
    Child
}

/// <summary>One visible line of the Processes table, computed off the UI objects so it can be tested alone.</summary>
public sealed record ProcessRowData(
    RowKind Kind,
    string Key,
    string Name,
    string Detail,
    string Path,
    string Publisher,
    IReadOnlyList<(int Pid, long CreateTime)> Processes,
    double Cpu,
    bool CpuSampled,
    long Memory,
    double Io,
    double NetworkRate,
    int Connections,
    bool Expandable,
    bool Expanded,
    bool IsApp);

public static class ProcessTree
{
    public const string SortName = "Name";
    public const string SortCpu = "Cpu";
    public const string SortMemory = "Memory";
    public const string SortIo = "Io";
    public const string SortNetwork = "Network";
    public const string SortBandwidth = "Bandwidth";
    public const string SortPublisher = "Publisher";
    public const string SortPath = "Path";

    private sealed class Group
    {
        public required string Key { get; init; }
        public List<ProcessSample> Members { get; } = new();
        public string Name { get; set; } = "";
        public bool IsApp { get; set; }
        public double Cpu;
        public bool CpuSampled;
        public long Memory;
        public double Io;
        public double NetworkRate;
        public int Connections;
    }

    public static List<ProcessRowData> Build(IReadOnlyList<ProcessSample> processes, string search, string sortColumn,
        bool descending, ISet<string> expandedKeys)
    {
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessSample process in processes)
        {
            if (process.Pid == 0) continue;
            string key = GroupKey(process);
            if (!groups.TryGetValue(key, out Group? group)) groups[key] = group = new Group { Key = key };
            group.Members.Add(process);
        }

        string query = search.Trim();
        var visible = new List<(Group Group, List<ProcessSample> Children, bool ForceExpand)>();
        foreach (Group group in groups.Values)
        {
            group.Name = GroupName(group.Members);
            group.IsApp = group.Members.Any(member => member.WindowTitle is not null);
            foreach (ProcessSample member in group.Members)
            {
                group.Cpu += member.CpuPercent;
                group.CpuSampled |= member.CpuSampled;
                group.Memory += member.PrivateWorkingSet;
                group.Io += member.IoBytesPerSecond;
                group.NetworkRate += member.NetworkBytesPerSecond;
                group.Connections += member.ConnectionCount;
            }

            if (query.Length == 0 || Matches(group.Name, query) || Matches(group.Members[0].Path, query) || Matches(group.Members[0].Company, query))
            {
                visible.Add((group, group.Members, false));
                continue;
            }

            List<ProcessSample> matchingChildren = group.Members.Where(member => ChildMatches(member, query)).ToList();
            if (matchingChildren.Count > 0) visible.Add((group, matchingChildren, true));
        }

        var rows = new List<ProcessRowData>(visible.Count + 16);
        AddSection(rows, "Apps", visible.Where(item => item.Group.IsApp), query, sortColumn, descending, expandedKeys);
        AddSection(rows, "Background processes", visible.Where(item => !item.Group.IsApp), query, sortColumn, descending, expandedKeys);
        return rows;
    }

    private static void AddSection(List<ProcessRowData> rows, string title,
        IEnumerable<(Group Group, List<ProcessSample> Children, bool ForceExpand)> items, string query,
        string sortColumn, bool descending, ISet<string> expandedKeys)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        rows.Add(new ProcessRowData(RowKind.Section, "section:" + title, $"{title} ({list.Count})", "", "", "",
            Array.Empty<(int, long)>(), 0, false, 0, 0, 0, 0, false, false, false));

        foreach (var (group, children, forceExpand) in SortGroups(list, sortColumn, descending))
        {
            ProcessSample first = group.Members[0];
            bool expandable = group.Members.Count > 1;
            bool expanded = expandable && (forceExpand || expandedKeys.Contains(group.Key));
            rows.Add(new ProcessRowData(RowKind.Group, group.Key, group.Name,
                expandable ? $"({group.Members.Count})" : "",
                first.Path, first.Company,
                group.Members.Select(member => member.Key).ToList(),
                group.Cpu, group.CpuSampled, group.Memory, group.Io, group.NetworkRate, group.Connections,
                expandable, expanded, group.IsApp));

            if (!expanded) continue;
            foreach (ProcessSample child in SortChildren(children, sortColumn, descending))
            {
                rows.Add(new ProcessRowData(RowKind.Child, $"{group.Key}|{child.Pid}|{child.CreateTime}", ChildName(child),
                    "PID " + child.Pid, child.Path, child.Company, new[] { child.Key },
                    child.CpuPercent, child.CpuSampled, child.PrivateWorkingSet, child.IoBytesPerSecond, child.NetworkBytesPerSecond, child.ConnectionCount,
                    false, false, group.IsApp));
            }
        }
    }

    private static IEnumerable<(Group Group, List<ProcessSample> Children, bool ForceExpand)> SortGroups(
        List<(Group Group, List<ProcessSample> Children, bool ForceExpand)> items, string column, bool descending)
    {
        Func<(Group Group, List<ProcessSample> Children, bool ForceExpand), object> key = column switch
        {
            SortCpu => item => item.Group.Cpu,
            SortMemory => item => item.Group.Memory,
            SortIo => item => item.Group.Io,
            SortNetwork => item => item.Group.Connections,
            SortBandwidth => item => item.Group.NetworkRate,
            SortPublisher => item => item.Group.Members[0].Company,
            SortPath => item => item.Group.Members[0].Path,
            _ => item => item.Group.Name
        };
        return Order(items, key, descending, item => item.Group.Name);
    }

    private static IEnumerable<ProcessSample> SortChildren(List<ProcessSample> children, string column, bool descending)
    {
        Func<ProcessSample, object> key = column switch
        {
            SortCpu => child => child.CpuPercent,
            SortMemory => child => child.PrivateWorkingSet,
            SortIo => child => child.IoBytesPerSecond,
            SortNetwork => child => child.ConnectionCount,
            SortBandwidth => child => child.NetworkBytesPerSecond,
            _ => child => ChildName(child)
        };
        return Order(children, key, descending, ChildName);
    }

    private static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, object> key, bool descending, Func<T, string> tieBreaker)
    {
        IComparer<object> comparer = Comparer<object>.Create((left, right) => left switch
        {
            string text => StringComparer.CurrentCultureIgnoreCase.Compare(text, right as string),
            _ => Comparer<object>.Default.Compare(left, right)
        });
        IOrderedEnumerable<T> ordered = descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
        return ordered.ThenBy(tieBreaker, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>Processes of one executable form one group; each service host stays separate, like Task Manager.</summary>
    internal static string GroupKey(ProcessSample process)
    {
        if (process.ImageName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)) return "pid:" + process.Pid;
        return process.Path.Length > 0 ? process.Path : "name:" + process.ImageName;
    }

    private static string GroupName(List<ProcessSample> members)
    {
        ProcessSample first = members[0];
        bool serviceHost = first.ImageName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase);
        if (serviceHost && first.Services is { Count: > 0 } services)
        {
            string name = "Service Host: " + services[0];
            return services.Count > 1 ? $"{name} +{services.Count - 1}" : name;
        }
        return first.DisplayName;
    }

    private static string ChildName(ProcessSample process)
    {
        if (!string.IsNullOrWhiteSpace(process.WindowTitle)) return process.WindowTitle;
        if (process.Services is { Count: > 0 } services) return string.Join(", ", services);
        return process.DisplayName;
    }

    private static bool ChildMatches(ProcessSample process, string query) =>
        Matches(process.ImageName, query) || Matches(process.WindowTitle, query) ||
        process.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture) == query ||
        (process.Services?.Any(service => Matches(service, query)) ?? false);

    private static bool Matches(string? value, string query) =>
        value is not null && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
