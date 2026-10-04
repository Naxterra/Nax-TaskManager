using System.Collections.ObjectModel;
using BetterTaskManager.Core.Startup;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

/// <summary>Task Manager's Startup apps: Run keys and startup folders, each with its on/off flag.</summary>
public sealed class StartupViewModel : ObservableObject
{
    public const string SortName = "Name";
    public const string SortPublisher = "Publisher";
    public const string SortStatus = "Status";
    public const string SortScope = "Scope";

    private List<StartupApp> apps = new();
    private TimeSpan? biosTime;
    private string summary = "";

    public StartupViewModel(AppSettings settings)
    {
        Layout = new ColumnLayout("Startup.", new Dictionary<string, double>
        {
            ["Name"] = 300, ["Publisher"] = 220, ["Status"] = 130, ["Scope"] = 160
        }, settings.ColumnWidths);
        StartupRow.SharedLayout = Layout;
    }

    public ColumnLayout Layout { get; }
    public ObservableCollection<StartupRow> Rows { get; } = new();
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string SortColumn { get; private set; } = SortName;
    public bool SortDescending { get; private set; }

    public async Task LoadAsync(string search)
    {
        apps = await Task.Run(StartupApps.Read);
        biosTime = StartupApps.LastBiosTime();
        Apply(search);
    }

    public void Apply(string search)
    {
        string query = search.Trim();
        IEnumerable<StartupApp> visible = apps.Where(app => query.Length == 0 ||
            app.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            app.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            app.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        IComparer<string> text = StringComparer.CurrentCultureIgnoreCase;
        Func<StartupApp, string> key = SortColumn switch
        {
            SortPublisher => app => app.Publisher,
            SortStatus => app => app.Enabled ? "0" : "1",
            SortScope => app => app.AllUsers ? "1" : "0",
            _ => app => app.DisplayName
        };
        var ordered = (SortDescending ? visible.OrderByDescending(key, text) : visible.OrderBy(key, text))
            .ThenBy(app => app.DisplayName, text).ToList();

        Rows.Clear();
        foreach (StartupApp app in ordered) Rows.Add(new StartupRow(app));
        Summary = Loc.F("Startup_Summary", apps.Count(app => app.Enabled), apps.Count(app => !app.Enabled)) +
            (biosTime is { } bios ? Loc.F("Startup_BiosTime", bios.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)) : "");
    }

    public void Sort(string column, string search)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        Apply(search);
    }
}

public sealed class StartupRow
{
    public StartupRow(StartupApp app)
    {
        App = app;
        Status = app.Enabled ? Loc.Get("Startup_Enabled") : Loc.Get("Startup_Disabled");
        // Null, not empty: an empty tooltip would still pop up as a blank box.
        StatusDetail = app.DisabledAt is { } at ? Loc.F("Startup_DisabledAt", at.ToString("g")) : null;
        Scope = app.AllUsers ? Loc.Get("Startup_AllUsers") : Loc.Get("Startup_CurrentUser");
        if (app.Locked) Status += Loc.Get("Startup_ByPolicy");
        Icon = IconCache.Get(app.ExecutablePath);
    }

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;

    public StartupApp App { get; }
    public string Name => App.DisplayName;
    public string Publisher => App.Publisher;
    public string Command => App.Command;
    public string Status { get; }
    public string? StatusDetail { get; }
    public string Scope { get; }
    public ImageSource? Icon { get; }
    public double Opacity => App.Enabled ? 1 : 0.6;

    /// <summary>What UI Automation and screen readers announce for the row.</summary>
    public override string ToString() => $"{Name}, {Status}";
}
