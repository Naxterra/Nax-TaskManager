using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BetterTaskManager.Fluent.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A list of reusable row slots. Each refresh writes new values into the existing slots instead of
/// removing and re-inserting rows, so the list never rebuilds its containers, keeps its scroll position and
/// only realized rows do binding work.
/// </summary>
public sealed class SlotCollection<TSlot, TData> : ObservableCollection<TSlot> where TSlot : new()
{
    private readonly Action<TSlot, TData> load;

    public SlotCollection(Action<TSlot, TData> load) => this.load = load;

    public void Apply(IReadOnlyList<TData> data)
    {
        for (int index = 0; index < data.Count; index++)
        {
            if (index >= Count) Add(new TSlot());
            load(this[index], data[index]);
        }
        while (Count > data.Count) RemoveAt(Count - 1);
    }
}

/// <summary>
/// Column widths shared by the header and every row template. Tables that pass <c>movable</c> also let the user
/// reorder those columns: the first column stays first, the movable ones follow in <see cref="Order"/>, and the
/// last (star-sized) column stays last. Templates bind their column definitions by position (<see cref="W1"/>...)
/// and each cell's Grid.Column to its column's position (<see cref="CpuColumn"/>...).
/// </summary>
public sealed class ColumnLayout : ObservableObject
{
    private readonly Dictionary<string, double> widths;
    private readonly Dictionary<string, double> minimums;

    private readonly Dictionary<string, double> saved;
    private readonly string prefix;
    private readonly string firstColumn;
    private readonly IReadOnlyList<string> defaultOrder;
    private readonly List<string> order;
    private readonly Dictionary<string, string>? savedOrders;

    public ColumnLayout(string prefix, Dictionary<string, double> defaults, Dictionary<string, double> saved,
        string[]? movable = null, Dictionary<string, string>? savedOrders = null)
    {
        this.prefix = prefix;
        this.saved = saved;
        this.savedOrders = savedOrders;
        firstColumn = defaults.Keys.First();
        minimums = defaults.ToDictionary(pair => pair.Key, pair => Math.Min(pair.Value, 60d));
        widths = new Dictionary<string, double>(defaults);
        foreach (string key in defaults.Keys)
        {
            if (saved.TryGetValue(prefix + key, out double value)) widths[key] = Math.Clamp(value, minimums[key], 900);
        }

        defaultOrder = movable ?? [];
        // Keep the saved order of known columns; columns added in a later version join at their default place.
        order = [];
        if (savedOrders is not null && savedOrders.TryGetValue(prefix, out string? text))
        {
            order.AddRange(text.Split(',').Where(defaultOrder.Contains).Distinct());
        }
        for (int index = 0; index < defaultOrder.Count; index++)
        {
            if (!order.Contains(defaultOrder[index])) order.Insert(Math.Min(index, order.Count), defaultOrder[index]);
        }
    }

    public IReadOnlyList<string> Order => order;

    public bool IsMovable(string column) => order.Contains(column);

    /// <summary>Grid column of a movable column (the first column is 0); -1 for others.</summary>
    public int ColumnOf(string column) => order.IndexOf(column) is int index and >= 0 ? index + 1 : -1;

    /// <summary>Moves a column to a 1-based position among the movable columns.</summary>
    public bool Move(string column, int position)
    {
        int from = order.IndexOf(column);
        int to = Math.Clamp(position, 1, order.Count) - 1;
        if (from < 0 || from == to) return false;
        order.RemoveAt(from);
        order.Insert(to, column);
        SaveOrder();
        Raise(string.Empty);
        return true;
    }

    public void ResetOrder()
    {
        order.Clear();
        order.AddRange(defaultOrder);
        SaveOrder();
        Raise(string.Empty);
    }

    public bool IsDefaultOrder => order.SequenceEqual(defaultOrder);

    private void SaveOrder()
    {
        if (savedOrders is null) return;
        if (IsDefaultOrder) savedOrders.Remove(prefix);
        else savedOrders[prefix] = string.Join(",", order);
    }

    /// <summary>The movable position (1-based) under a header x coordinate.</summary>
    public int PositionAt(double x)
    {
        double edge = widths[firstColumn];
        for (int index = 0; index < order.Count; index++)
        {
            edge += widths[order[index]];
            if (x < edge) return index + 1;
        }
        return order.Count;
    }

    /// <summary>Left edge, in header coordinates, of a movable position (1-based).</summary>
    public double LeftEdgeOf(int position)
    {
        double edge = widths[firstColumn];
        for (int index = 0; index < position - 1 && index < order.Count; index++) edge += widths[order[index]];
        return edge;
    }

    public double WidthOf(int position) => position >= 1 && position <= order.Count ? widths[order[position - 1]] : 0;

    private GridLength At(int position) => new(WidthOf(position));

    public GridLength W1 => At(1);
    public GridLength W2 => At(2);
    public GridLength W3 => At(3);
    public GridLength W4 => At(4);
    public GridLength W5 => At(5);
    public GridLength W6 => At(6);
    public GridLength W7 => At(7);

    public int CpuColumn => ColumnOf("Cpu");
    public int MemoryColumn => ColumnOf("Memory");
    public int IoColumn => ColumnOf("Io");
    public int BandwidthColumn => ColumnOf("Bandwidth");
    public int NetworkColumn => ColumnOf("Network");
    public int GpuColumn => ColumnOf("Gpu");
    public int PublisherColumn => ColumnOf("Publisher");
    public int PidColumn => ColumnOf("Pid");
    public int StatusColumn => ColumnOf("Status");
    public int UserColumn => ColumnOf("User");
    public int LocalColumn => ColumnOf("Local");
    public int RemoteColumn => ColumnOf("Remote");
    public int ScopeColumn => ColumnOf("Scope");
    public int StateColumn => ColumnOf("State");
    public int DataColumn => ColumnOf("Data");
    public int SpeedColumn => ColumnOf("Speed");

    /// <summary>
    /// Columns a wide cell may cover (the Network page's per-app summary covers the address columns that app rows
    /// leave empty). <see cref="SpanColumn"/>/<see cref="SpanCount"/> give the longest unbroken run of them.
    /// </summary>
    public string[] SpanGroup { get; init; } = [];

    public int SpanColumn => LongestSpan().Column;
    public int SpanCount => LongestSpan().Count;

    private (int Column, int Count) LongestSpan()
    {
        int bestStart = 0, bestCount = 0, runStart = 0, runCount = 0;
        for (int index = 0; index < order.Count; index++)
        {
            if (!SpanGroup.Contains(order[index])) { runCount = 0; continue; }
            if (runCount == 0) runStart = index;
            runCount++;
            if (runCount > bestCount) (bestStart, bestCount) = (runStart, runCount);
        }
        return bestCount == 0 ? (1, 1) : (bestStart + 1, bestCount);
    }

    public GridLength this[string column] => new(widths.TryGetValue(column, out double width) ? width : 0);

    public GridLength Name => this["Name"];
    public GridLength Cpu => this["Cpu"];
    public GridLength Memory => this["Memory"];
    public GridLength Io => this["Io"];
    public GridLength Network => this["Network"];
    public GridLength Bandwidth => this["Bandwidth"];
    public GridLength Gpu => this["Gpu"];
    public GridLength Publisher => this["Publisher"];
    public GridLength Local => this["Local"];
    public GridLength Remote => this["Remote"];
    public GridLength State => this["State"];
    public GridLength Speed => this["Speed"];
    public GridLength Data => this["Data"];
    public GridLength Scope => this["Scope"];
    public GridLength Pid => this["Pid"];
    public GridLength Status => this["Status"];
    public GridLength User => this["User"];
    public GridLength Priority => this["Priority"];

    public void Resize(string column, double delta)
    {
        if (!widths.TryGetValue(column, out double width)) return;
        double next = Math.Clamp(width + delta, minimums[column], 900);
        if (Math.Abs(next - width) < 0.5) return;
        widths[column] = next;
        saved[prefix + column] = next;
        Raise(column);
        if (ColumnOf(column) is int position and >= 1 and <= 7) Raise("W" + position);
    }
}

/// <summary>Task Manager-style cell tint: stronger amber for heavier resource use.</summary>
public static class Heat
{
    private static readonly byte[] Alphas = [0, 26, 46, 70, 96, 124, 156];
    private static SolidColorBrush[]? s_brushes;

    public static Brush Level(int level)
    {
        s_brushes ??= Alphas.Select(alpha => new SolidColorBrush(alpha == 0 ? Colors.Transparent : Color.FromArgb(alpha, 255, 170, 51))).ToArray();
        return s_brushes[Math.Clamp(level, 0, s_brushes.Length - 1)];
    }

    public static int Scale(double value, params double[] thresholds)
    {
        int level = 0;
        foreach (double threshold in thresholds)
        {
            if (value >= threshold) level++;
            else break;
        }
        return level;
    }
}
