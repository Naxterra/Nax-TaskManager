using System.Diagnostics;

namespace BetterTaskManager.Fluent.Services;

/// <summary>A bounded ring of timestamped values covering a fixed elapsed-time window.</summary>
public sealed class HistoryBuffer
{
    private readonly double[] values;
    private readonly long[] timestamps;
    private int start;

    public HistoryBuffer(int windowSeconds)
    {
        if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        WindowSeconds = windowSeconds;
        values = new double[windowSeconds * 4 + 1]; // 250 ms is the fastest supported refresh.
        timestamps = new long[values.Length];
    }

    public int Count { get; private set; }
    public int Capacity => values.Length;
    public double WindowSeconds { get; }
    public double this[int index] => values[(start + index) % Capacity];

    public void Add(double value, long? timestamp = null)
    {
        long at = timestamp ?? Stopwatch.GetTimestamp();
        if (Count > 0 && at <= timestamps[(start + Count - 1) % Capacity]) return; // same snapshot redrawn
        while (Count > 0 && Stopwatch.GetElapsedTime(timestamps[start], at).TotalSeconds > WindowSeconds)
        {
            start = (start + 1) % Capacity;
            Count--;
        }
        if (Count == Capacity) { start = (start + 1) % Capacity; Count--; }
        int next = (start + Count) % Capacity;
        values[next] = value;
        timestamps[next] = at;
        Count++;
    }

    public double AgeSeconds(int index) => Count == 0 ? 0 :
        Stopwatch.GetElapsedTime(timestamps[(start + index) % Capacity], timestamps[(start + Count - 1) % Capacity]).TotalSeconds;

    public double Max()
    {
        double max = 0;
        for (int index = 0; index < Count; index++) max = Math.Max(max, this[index]);
        return max;
    }
}
