using BetterTaskManager.Core.Network;
using System.Globalization;
using BetterTaskManager.Core.History;
using BetterTaskManager.Fluent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

public enum HistoryRange
{
    Today,
    Week,
    Month
}

public sealed record AppUsageRow(string Key, string Name, string Path, string Detail, string Total, string Split, double Fraction);

public sealed class AppUsageSlot : ObservableObject
{
    public const double BarMaxWidth = 160;
    private string key = "", name = "", path = "", detail = "", total = "", split = "";
    private double barWidth;
    private ImageSource? icon;

    public string Key { get => key; private set => Set(ref key, value); }
    public string Name { get => name; private set => Set(ref name, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public string Detail { get => detail; private set => Set(ref detail, value); }
    public string Total { get => total; private set => Set(ref total, value); }
    public string Split { get => split; private set => Set(ref split, value); }
    public double BarWidth { get => barWidth; private set => Set(ref barWidth, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }

    public static void Load(AppUsageSlot slot, AppUsageRow row)
    {
        if (slot.Path != row.Path || slot.Icon is null) slot.Icon = row.Key.Length == 0 ? null : IconCache.Get(row.Path);
        slot.Key = row.Key;
        slot.Name = row.Name;
        slot.Path = row.Path;
        slot.Detail = row.Detail;
        slot.Total = row.Total;
        slot.Split = row.Split;
        slot.BarWidth = Math.Max(row.Fraction > 0 ? 2 : 0, row.Fraction * BarMaxWidth);
    }
}

public sealed record ConnectionLogRow(long Id, string Time, string TimeDetail, string App, string Path, string Remote, string RemoteDetail, string Protocol, string Data);

public sealed class ConnectionLogSlot : ObservableObject
{
    private string time = "", timeDetail = "", app = "", path = "", remote = "", remoteDetail = "", protocol = "", data = "";
    private ImageSource? icon;

    public long Id { get; private set; }
    public string Time { get => time; private set => Set(ref time, value); }
    public string TimeDetail { get => timeDetail; private set => Set(ref timeDetail, value); }
    public string App { get => app; private set => Set(ref app, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public string Remote { get => remote; private set => Set(ref remote, value); }
    public string RemoteDetail { get => remoteDetail; private set => Set(ref remoteDetail, value); }
    public string Protocol { get => protocol; private set => Set(ref protocol, value); }
    public string Data { get => data; private set => Set(ref data, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }

    public static void Load(ConnectionLogSlot slot, ConnectionLogRow row)
    {
        if (slot.Path != row.Path || slot.Icon is null) slot.Icon = IconCache.Get(row.Path);
        slot.Id = row.Id;
        slot.Time = row.Time;
        slot.TimeDetail = row.TimeDetail;
        slot.App = row.App;
        slot.Path = row.Path;
        slot.Remote = row.Remote;
        slot.RemoteDetail = row.RemoteDetail;
        slot.Protocol = row.Protocol;
        slot.Data = row.Data;
    }
}

/// <summary>Reads the background service's history database (read-only) for the History page.</summary>
public sealed class HistoryViewModel
{
    public const int ConnectionLimit = 1000;

    public SlotCollection<AppUsageSlot, AppUsageRow> Apps { get; } = new(AppUsageSlot.Load);
    public SlotCollection<ConnectionLogSlot, ConnectionLogRow> Connections { get; } = new(ConnectionLogSlot.Load);

    public HistoryRange Range { get; set; } = HistoryRange.Today;
    /// <summary>DNS lookups are frequent; the log hides them unless asked.</summary>
    public bool IncludeDns { get; set; }
    /// <summary>Null shows all apps.</summary>
    public string? AppKey { get; set; }
    public HistoryServiceState ServiceState { get; private set; }
    public string Summary { get; private set; } = "";
    public string ConnectionSummary { get; private set; } = "";
    public bool HasData { get; private set; }

    private sealed record LoadResult(List<AppUsage> Apps, List<ConnectionRecord> Connections, DateTime? LastWrite, string? Error);

    public async Task LoadAsync(string search)
    {
        HistoryRange range = Range;
        string? appKey = AppKey;
        bool includeDns = IncludeDns;
        DateTime fromDay = DateTime.Today.AddDays(range switch { HistoryRange.Week => -6, HistoryRange.Month => -29, _ => 0 });
        ServiceState = HistoryServiceControl.QueryState();

        LoadResult result = await Task.Run(() =>
        {
            try
            {
                using HistoryStore? store = HistoryStore.OpenReadOnly(HistoryStore.DefaultPath);
                if (store is null) return new LoadResult([], [], null, null);
                return new LoadResult(
                    store.ReadAppUsage(fromDay),
                    store.ReadConnections(fromDay.ToUniversalTime(), appKey, search, ConnectionLimit, includeDns),
                    store.ReadLastWrite(),
                    null);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                return new LoadResult([], [], null, ex.Message);
            }
        });

        if (result.Error is not null)
        {
            // Keep what is shown; a busy database is retried on the next refresh.
            Summary = Loc.F("History_ReadError", result.Error);
            return;
        }

        HasData = result.LastWrite is not null;
        Summary = ServiceState switch
        {
            HistoryServiceState.Running => result.LastWrite is { } last ? Loc.F("History_RecordingSaved", last.ToLocalTime().ToString("T")) : Loc.Get("History_Recording"),
            HistoryServiceState.NotInstalled => HasData ? Loc.Get("History_OffWithData") : Loc.Get("History_Off"),
            _ => Loc.Get("History_ServiceStopped")
        };

        long maxTotal = Math.Max(1, result.Apps.Count > 0 ? result.Apps.Max(app => app.Total) : 1);
        // VPN tunnels carry the other apps' traffic again; the total would count it twice.
        List<AppUsage> counted = result.Apps.Where(app => !app.Tunnel).ToList();
        var apps = new List<AppUsageRow>(result.Apps.Count + 1);
        if (result.Apps.Count > 0) apps.Add(
            new AppUsageRow("", Loc.Get("History_AllApps"), "", Loc.F("Common_Connections", Format.Count(result.Apps.Sum(app => app.Connections))) + (counted.Count < result.Apps.Count ? Loc.Get("History_VpnExcluded") : ""),
                Format.Bytes(counted.Sum(app => app.Total)), Split(counted.Sum(app => app.BytesIn), counted.Sum(app => app.BytesOut)), 0));
        foreach (AppUsage app in result.Apps)
        {
            apps.Add(new AppUsageRow(app.AppKey, Loc.AppName(app.AppName), app.AppPath,
                (app.Connections == 1 ? Loc.Get("Common_OneConnection") : Loc.F("Common_Connections", Format.Count(app.Connections))) + (app.Tunnel ? Loc.Get("History_TunnelNotInTotal") : ""),
                app.Total > 0 ? Format.Bytes(app.Total) : "", app.Total > 0 ? Split(app.BytesIn, app.BytesOut) : "",
                app.Total / (double)maxTotal));
        }
        Apps.Apply(apps);

        Connections.Apply(result.Connections.Select(ToRow).ToList());
        ConnectionSummary = result.Connections.Count >= ConnectionLimit
            ? Loc.F("History_LatestConnections", Format.Count(ConnectionLimit))
            : Loc.F("Common_Connections", Format.Count(result.Connections.Count));
    }

    private static ConnectionLogRow ToRow(ConnectionRecord record)
    {
        DateTime first = record.FirstSeen.ToLocalTime();
        DateTime last = record.LastSeen.ToLocalTime();
        string time = first.Date == DateTime.Today ? first.ToString("T", CultureInfo.CurrentCulture) : first.ToString("g", CultureInfo.CurrentCulture);
        string timeDetail = Loc.F("History_TimeDetail", first.ToString("G"), last.ToString("G"), Format.Duration(last - first));

        string endpoint = record.RemoteAddress.Contains(':') ? $"[{record.RemoteAddress}]:{record.RemotePort}" : $"{record.RemoteAddress}:{record.RemotePort}";
        string remote = record.RemoteHost is { } host ? $"{host}:{record.RemotePort}" : endpoint;
        string remoteDetail = record.RemoteHost is null ? endpoint
            : record.RemoteHostIsReverse ? Loc.F("Remote_Reverse", endpoint)
            : Loc.F("Remote_Lookup", endpoint);

        if (record.Protocol == "DNS")
        {
            string answers = record.LocalAddress.Length > 0 ? record.LocalAddress : Loc.Get("Dns_NoAddresses");
            return new ConnectionLogRow(record.Id, time, timeDetail, Loc.AppName(record.AppName), record.AppPath, record.RemoteHost ?? record.RemoteAddress,
                Loc.F("Dns_Detail", answers), Loc.Get("Dns_Lookup"), "–");
        }
        string protocol = record.Protocol == "UDP" ? "UDP" : "TCP · " + Loc.State(record.State);
        if (record.Scope.Length > 0) protocol += " · " + Loc.Scope(record.Scope) + (record.Inbound ? Loc.Get("Scope_Inbound") : "");
        string data = record.BytesIn + record.BytesOut > 0 ? Split(record.BytesIn, record.BytesOut) : "–";
        return new ConnectionLogRow(record.Id, time, timeDetail, Loc.AppName(record.AppName), record.AppPath, remote, remoteDetail, protocol, data);
    }

    private static string Split(long received, long sent) => $"↓ {Format.Bytes(received)}  ↑ {Format.Bytes(sent)}";
}
