using BetterTaskManager.Core.History;

namespace BetterTaskManager.Fluent.Services;

internal sealed record BlockedRowModel(BlockedRecord Record, string Time)
{
    internal bool HasSameContentAs(BlockedRecord previous) =>
        Record with { Count = previous.Count, LastSeen = previous.LastSeen } == previous;
}

/// <summary>Reconciles the displayed blocked groups without recreating unchanged row content.</summary>
internal sealed class BlockedLogRows<TView> where TView : class
{
    internal const int Limit = 300;
    private readonly Dictionary<long, (BlockedRowModel Model, TView View)> entries = new();

    internal List<BlockedRecord> Apply(IList<TView> rows, IEnumerable<BlockedRecord> records, Func<BlockedRecord, string> time,
        Func<BlockedRowModel, TView> create, Action<TView, BlockedRowModel> update)
    {
        List<BlockedRecord> shown = records.Take(Limit).ToList();
        var wanted = shown.Select(record => record.Id).ToHashSet();
        foreach (long id in entries.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            rows.Remove(entries[id].View);
            entries.Remove(id);
        }
        for (int index = 0; index < shown.Count; index++)
        {
            BlockedRecord record = shown[index];
            var model = new BlockedRowModel(record, time(record));
            if (!entries.TryGetValue(record.Id, out var entry))
            {
                entry = (model, create(model));
            }
            else if (entry.Model != model)
            {
                update(entry.View, model);
                entry = (model, entry.View);
            }
            entries[record.Id] = entry;
            if (index < rows.Count && ReferenceEquals(rows[index], entry.View)) continue;
            rows.Remove(entry.View);
            rows.Insert(index, entry.View);
        }
        return shown;
    }
}
