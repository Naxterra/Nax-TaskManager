namespace BetterTaskManager.Fluent.Services;

/// <summary>Keeps each page's query separate while the window shares one search box.</summary>
internal sealed class PageSearchState
{
    private readonly Dictionary<string, string> queries = new(StringComparer.Ordinal);

    internal string Page { get; private set; } = "Overview";
    internal string Text => GetForPage(Page);

    internal string GetForPage(string page) => queries.GetValueOrDefault(page, "");

    internal void SetForPage(string page, string? text) => queries[page] = text ?? "";

    internal string Select(string page)
    {
        Page = page;
        return Text;
    }

    /// <summary>Typing on a page without a searchable list opens Processes with that query.</summary>
    internal string Edit(string? text, bool fromUser)
    {
        text ??= "";
        if (fromUser && text.Length > 0 && !HasSearchableList(Page)) Page = "Processes";
        SetForPage(Page, text);
        return Page;
    }

    private static bool HasSearchableList(string page) => page is "Processes" or "Network" or "Disk" or "History" or "Startup";
}
