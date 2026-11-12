namespace BetterTaskManager.Fluent.Services;

/// <summary>Independent, persisted sorting for the app list and its connections table.</summary>
internal sealed class NetworkSortState
{
    private readonly AppSettings settings;
    public static readonly string[] AppColumns = ["Name", "Data", "Speed", "Download", "Upload"];
    public static readonly string[] ConnectionColumns = ["Remote", "App", "Local", "Scope", "State", "Download", "Upload"];

    public NetworkSortState(AppSettings settings)
    {
        this.settings = settings;
        if (settings.ColumnOrder.TryGetValue("Connections.", out string? order) && order.Split(',').Contains("Speed"))
            settings.ColumnOrder["Connections."] = string.Join(",", order.Split(',').SelectMany(column => column == "Speed" ? new[] { "Download", "Upload" } : new[] { column }));
        if (settings.ColumnWidths.TryGetValue("Connections.Speed", out double width))
        {
            settings.ColumnWidths.TryAdd("Connections.Download", width / 2);
            settings.ColumnWidths.TryAdd("Connections.Upload", width / 2);
        }
        // Older settings used one choice for both lists. Keep a connection choice without blanking the app selector.
        if (settings.NetworkConnectionSortColumn is null)
        {
            bool legacyConnection = settings.NetworkSortColumn is "Remote" or "State" or "Speed";
            settings.NetworkConnectionSortColumn = legacyConnection ? settings.NetworkSortColumn : "Remote";
            settings.NetworkConnectionSortDescending = legacyConnection && settings.NetworkSortDescending;
        }
        if (settings.NetworkConnectionSortColumn == "Speed") settings.NetworkConnectionSortColumn = "Download";
        if (!AppColumns.Contains(settings.NetworkSortColumn))
        {
            settings.NetworkSortColumn = "Name";
            settings.NetworkSortDescending = false;
        }
        if (!ConnectionColumns.Contains(settings.NetworkConnectionSortColumn))
        {
            settings.NetworkConnectionSortColumn = "Remote";
            settings.NetworkConnectionSortDescending = false;
        }
    }

    public string AppColumn => settings.NetworkSortColumn;
    public bool AppsDescending => settings.NetworkSortDescending;
    public string ConnectionColumn => settings.NetworkConnectionSortColumn!;
    public bool ConnectionsDescending => settings.NetworkConnectionSortDescending;

    public void SelectApps(string column)
    {
        if (!AppColumns.Contains(column)) throw new ArgumentOutOfRangeException(nameof(column));
        settings.NetworkSortColumn = column;
        settings.NetworkSortDescending = column is "Data" or "Speed" or "Download" or "Upload";
    }

    public void SelectConnections(string column, bool toggle = false)
    {
        if (!ConnectionColumns.Contains(column)) throw new ArgumentOutOfRangeException(nameof(column));
        settings.NetworkConnectionSortDescending = toggle && ConnectionColumn == column
            ? !ConnectionsDescending : column is "State" or "Speed" or "Download" or "Upload";
        settings.NetworkConnectionSortColumn = column;
    }

    public void ToggleApps() => settings.NetworkSortDescending = !AppsDescending;
    public void ToggleConnections() => settings.NetworkConnectionSortDescending = !ConnectionsDescending;
}
