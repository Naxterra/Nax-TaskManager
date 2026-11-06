using System.Collections.Concurrent;

namespace BetterTaskManager.Core.Network;

/// <summary>Keeps short-lived process identities until their pending traffic has been attributed.</summary>
internal sealed class TracedProcessCache(TimeProvider clock)
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<int, BandwidthMonitor.TracedProcess> entries = new();
    private readonly object sweepGate = new();
    private long? sweptAt;

    internal int Count => entries.Count;

    internal void Start(int pid, BandwidthMonitor.TracedProcess process) => entries[pid] = process;

    internal void Stop(int pid)
    {
        if (entries.TryGetValue(pid, out var known))
            entries.TryUpdate(pid, known with { ExitedUtc = clock.GetUtcNow().UtcDateTime }, known);
    }

    internal bool TryGet(int pid, out BandwidthMonitor.TracedProcess process)
    {
        while (entries.TryGetValue(pid, out process))
        {
            if (!Expired(process, clock.GetUtcNow().UtcDateTime)) return true;
            // A concurrent start may have replaced this PID after the lookup. Only remove the observed identity.
            if (RemoveExpired(pid, process, clock.GetUtcNow().UtcDateTime)) break;
        }
        process = default;
        return false;
    }

    internal bool RemoveExpired(int pid, BandwidthMonitor.TracedProcess observed, DateTime now) =>
        Expired(observed, now) && ((ICollection<KeyValuePair<int, BandwidthMonitor.TracedProcess>>)entries).Remove(new(pid, observed));

    private static bool Expired(BandwidthMonitor.TracedProcess process, DateTime now) =>
        process.ExitedUtc is { } exited && now - exited > Retention;

    internal void Sweep()
    {
        lock (sweepGate)
        {
            long ticks = clock.GetTimestamp();
            if (sweptAt is { } previous && clock.GetElapsedTime(previous, ticks) < SweepInterval) return;
            sweptAt = ticks;
            DateTime now = clock.GetUtcNow().UtcDateTime;
            foreach (var pair in entries) RemoveExpired(pair.Key, pair.Value, now);
        }
    }
}
