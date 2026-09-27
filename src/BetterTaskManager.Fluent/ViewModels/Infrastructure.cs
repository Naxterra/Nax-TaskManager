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

/// <summary>Column widths shared by the header and every row template.</summary>
public sealed class ColumnLayout : ObservableObject
{
    private readonly Dictionary<string, double> widths;
    private readonly Dictionary<string, double> minimums;

    private readonly Dictionary<string, double> saved;
    private readonly string prefix;

    public ColumnLayout(string prefix, Dictionary<string, double> defaults, Dictionary<string, double> saved)
    {
        this.prefix = prefix;
        this.saved = saved;
        minimums = defaults.ToDictionary(pair => pair.Key, pair => Math.Min(pair.Value, 60d));
        widths = new Dictionary<string, double>(defaults);
        foreach (string key in defaults.Keys)
        {
            if (saved.TryGetValue(prefix + key, out double value)) widths[key] = Math.Clamp(value, minimums[key], 900);
        }
    }

    public GridLength this[string column] => new(widths.TryGetValue(column, out double width) ? width : 0);

    public GridLength Name => this["Name"];
    public GridLength Cpu => this["Cpu"];
    public GridLength Memory => this["Memory"];
    public GridLength Io => this["Io"];
    public GridLength Network => this["Network"];
    public GridLength Publisher => this["Publisher"];
    public GridLength Local => this["Local"];
    public GridLength Remote => this["Remote"];
    public GridLength State => this["State"];

    public void Resize(string column, double delta)
    {
        if (!widths.TryGetValue(column, out double width)) return;
        double next = Math.Clamp(width + delta, minimums[column], 900);
        if (Math.Abs(next - width) < 0.5) return;
        widths[column] = next;
        saved[prefix + column] = next;
        Raise(column);
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
