using BetterTaskManager.Core.Firewall;

namespace BetterTaskManager.Core.History;

/// <summary>
/// Groups firewall drops and writes them to the history store. Apps retry blocked connections many times; drops of
/// the same app, direction, protocol, remote end (or local port for incoming ones) and reason within 10 minutes of
/// each other become one record whose count and last time grow. Not thread-safe: the service calls it from its
/// snapshot thread, the same thread that writes connections.
/// </summary>
public sealed class BlockedRecorder(HistoryStore store, Func<string, string?> resolveHost)
{
    private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(10);
    private const int MaxGroups = 2000;

    private readonly Dictionary<GroupKey, Group> groups = new();

    /// <summary>Adds drops; <paramref name="pathCase"/> turns the engine's lower-case path into the file's own spelling where known.</summary>
    public void Record(IEnumerable<BlockedEvent> events, Func<string, string> pathCase)
    {
        foreach (BlockedEvent drop in events)
        {
            string path = pathCase(drop.AppPath);
            var key = new GroupKey(path.ToLowerInvariant(), drop.Inbound, drop.Protocol, drop.RemoteAddress, drop.Inbound ? drop.LocalPort : drop.RemotePort, drop.Ours, drop.Reason);
            if (groups.TryGetValue(key, out Group? group) && drop.TimeUtc - group.LastSeen < GroupWindow)
            {
                group.Count++;
                if (drop.TimeUtc > group.LastSeen) group.LastSeen = drop.TimeUtc;
                group.Dirty = true;
                continue;
            }
            groups[key] = new Group(path, drop) { Dirty = true };
        }
    }

    /// <summary>Writes new and grown groups and forgets groups that ended more than 10 minutes ago.</summary>
    public void Flush(DateTime nowUtc)
    {
        List<Group> dirty = groups.Values.Where(group => group.Dirty).ToList();
        if (dirty.Count > 0)
        {
            var records = dirty.Select(group => new BlockedRecord(group.Id, group.FirstSeen, group.LastSeen, group.Path, group.Event.Inbound, group.Event.Protocol,
                group.Event.RemoteAddress, group.Event.RemoteAddress.Length > 0 ? resolveHost(group.Event.RemoteAddress) : null,
                group.Event.RemotePort, group.Event.LocalPort, group.Event.Ours, group.Event.Reason, group.Count)).ToList();
            List<long> ids = store.WriteBlocked(records);
            int next = 0;
            foreach (Group group in dirty)
            {
                if (group.Id == 0) group.Id = ids[next++];
                group.Dirty = false;
            }
        }
        foreach (var (key, group) in groups.Where(pair => nowUtc - pair.Value.LastSeen > GroupWindow || groups.Count > MaxGroups && !pair.Value.Dirty).ToList())
        {
            groups.Remove(key);
        }
    }

    private readonly record struct GroupKey(string Path, bool Inbound, int Protocol, string Remote, int Port, bool Ours, string Reason);

    private sealed class Group(string path, BlockedEvent first)
    {
        public string Path { get; } = path;
        public BlockedEvent Event { get; } = first;
        public long Id { get; set; }
        public DateTime FirstSeen { get; } = first.TimeUtc;
        public DateTime LastSeen { get; set; } = first.TimeUtc;
        public int Count { get; set; } = 1;
        public bool Dirty { get; set; }
    }
}
