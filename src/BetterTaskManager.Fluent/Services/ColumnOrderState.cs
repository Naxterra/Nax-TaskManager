namespace BetterTaskManager.Fluent.Services;

/// <summary>Column positions shared by header dragging, move commands and row bindings.</summary>
internal sealed class ColumnOrderState
{
    private readonly string[] defaults;
    private readonly List<string> columns = [];

    public ColumnOrderState(string[] defaults, string? savedOrder, bool movableFirst = false)
    {
        this.defaults = defaults;
        FirstPosition = movableFirst ? 0 : 1;
        if (!string.IsNullOrWhiteSpace(savedOrder))
            columns.AddRange(savedOrder.Split(',').Where(defaults.Contains).Distinct());
        for (int index = 0; index < defaults.Length; index++)
            if (!columns.Contains(defaults[index])) columns.Insert(Math.Min(index, columns.Count), defaults[index]);
    }

    public IReadOnlyList<string> Columns => columns;
    public int FirstPosition { get; }
    public int LastPosition => FirstPosition + columns.Count - 1;
    public bool IsDefault => columns.SequenceEqual(defaults);
    public int PositionOf(string column) => columns.IndexOf(column) is int index and >= 0 ? index + FirstPosition : -1;
    public string? ColumnAt(int position) => position >= FirstPosition && position <= LastPosition ? columns[position - FirstPosition] : null;

    public bool Move(string column, int position)
    {
        int from = columns.IndexOf(column);
        if (from < 0) return false;
        int to = Math.Clamp(position, FirstPosition, LastPosition) - FirstPosition;
        if (from == to) return false;
        columns.RemoveAt(from);
        columns.Insert(to, column);
        return true;
    }

    public void Reset()
    {
        columns.Clear();
        columns.AddRange(defaults);
    }

    public int PositionAt(double x, IReadOnlyDictionary<string, double> widths, double leadingWidth)
    {
        double edge = leadingWidth;
        for (int index = 0; index < columns.Count; index++)
        {
            edge += widths[columns[index]];
            if (x < edge) return index + FirstPosition;
        }
        return LastPosition;
    }

    public double LeftEdgeOf(int position, IReadOnlyDictionary<string, double> widths, double leadingWidth)
    {
        double edge = leadingWidth;
        for (int index = 0; index < position - FirstPosition && index < columns.Count; index++) edge += widths[columns[index]];
        return edge;
    }
}
