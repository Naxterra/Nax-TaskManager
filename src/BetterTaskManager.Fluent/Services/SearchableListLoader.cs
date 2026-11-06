namespace BetterTaskManager.Fluent.Services;

/// <summary>Reads list data in the background and applies the current query when the read finishes.</summary>
internal static class SearchableListLoader
{
    internal static async Task LoadAsync<T>(Func<T> read, Func<string> currentSearch, Action<T, string> apply)
    {
        T data = await Task.Run(read);
        apply(data, currentSearch());
    }
}
