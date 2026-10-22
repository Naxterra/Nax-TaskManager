using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Controls;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace BetterTaskManager.Fluent.Views;

/// <summary>Live system overview; all figures come from the monitor's shared snapshots.</summary>
public sealed partial class OverviewPage : Page
{
    private readonly StackPanel root = new() { Spacing = 18, Padding = new Thickness(28, 20, 28, 24) };
    private readonly TextBlock summary = Text("", 13, secondary: true), live = Text("", 11), uptime = Text("", 12, secondary: true);
    private readonly TextBlock memoryValue = Text("", 30, strong: true), memoryDetail = Text("", 12, secondary: true);
    private readonly TextBlock availableValue = Text("", 13, strong: true), committedValue = Text("", 13, strong: true);
    private readonly TextBlock processCount = Text("", 12, secondary: true), connectionCount = Text("", 12, secondary: true), noConnections = Text("", 13, secondary: true);
    private readonly ProgressBar memoryBar = new() { Maximum = 100, Height = 6, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Grid metrics = new() { ColumnSpacing = 12, RowSpacing = 12 }, charts = new() { ColumnSpacing = 18, RowSpacing = 18 }, lists = new() { ColumnSpacing = 18, RowSpacing = 18 };
    private readonly StatTile cpu = new(), memory = new(), down = new(), up = new();
    private readonly TrendChart activity = new() { Height = 132, Filled = true };
    private readonly StackPanel apps = new(), destinations = new();
    private readonly Border activityCard, memoryCard, appsCard, networkCard;
    private int layoutMode = -1;

    public OverviewPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnSpacing = 18 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 5 };
        var eyebrow = Text(Loc.Get("Design_Eyebrow"), 10, strong: true); eyebrow.CharacterSpacing = 140; eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");
        titles.Children.Add(eyebrow); titles.Children.Add(Text(Loc.Get("Design_OverviewTitle"), 32, strong: true)); titles.Children.Add(summary); header.Children.Add(titles);
        var right = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; var dot = Dot("BrandBrush"); badge.Children.Add(dot); badge.Children.Add(live);
        var pill = new Border { Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(14), Child = badge, HorizontalAlignment = HorizontalAlignment.Right };
        pill.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush"); live.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");
        right.Children.Add(pill); right.Children.Add(uptime); Grid.SetColumn(right, 1); header.Children.Add(right); root.Children.Add(header);

        StatTile[] tiles = [cpu, memory, down, up];
        string[] captions = ["Tile_Cpu", "Tile_Memory", "Tile_Download", "Tile_Upload"], colors = ["ChartCpuBrush", "ChartMemoryBrush", "ChartReceiveBrush", "ChartSendBrush"];
        for (int i = 0; i < tiles.Length; i++) { tiles[i].Caption = Loc.Get(captions[i]); tiles[i].SetResourceReference(StatTile.StrokeProperty, colors[i]); metrics.Children.Add(tiles[i]); }
        root.Children.Add(metrics);
        var plot = new StackPanel { Spacing = 12 }; plot.Children.Add(Heading(Loc.Get("Design_Activity"), Loc.Get("Design_LastSamples")));
        activity.SetResourceReference(TrendChart.StrokeProperty, "ChartCpuBrush"); activity.SetResourceReference(TrendChart.SecondStrokeProperty, "ChartMemoryBrush"); plot.Children.Add(activity);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 }; legend.Children.Add(Legend("CPU", "ChartCpuBrush")); legend.Children.Add(Legend(Loc.Get("Tile_Memory"), "ChartMemoryBrush")); plot.Children.Add(legend);
        activityCard = Card(plot); charts.Children.Add(activityCard);
        var mem = new StackPanel { Spacing = 6 }; mem.Children.Add(Text(Loc.Get("Design_MemoryFootprint"), 14, strong: true)); mem.Children.Add(memoryValue); mem.Children.Add(memoryDetail);
        memoryBar.SetResourceReference(Control.ForegroundProperty, "ChartMemoryBrush"); mem.Children.Add(memoryBar); mem.Children.Add(Fact(Loc.Get("Design_Available"), availableValue)); mem.Children.Add(Fact(Loc.Get("Design_Committed"), committedValue));
        var memoryLink = new HyperlinkButton { Content = Loc.Get("Memory_Open"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        memoryLink.Click += (_, _) => App.Window.NavigatePage("Memory"); mem.Children.Add(memoryLink);
        memoryCard = Card(mem); charts.Children.Add(memoryCard); root.Children.Add(charts);
        var appContent = new StackPanel { Spacing = 8 }; appContent.Children.Add(HeadingLink(Loc.Get("Design_ResourceLeaders"), "Processes")); appContent.Children.Add(processCount);
        appContent.Children.Add(ColumnLabels(Loc.Get("Design_Application"), "CPU", Loc.Get("Tile_Memory"))); appContent.Children.Add(apps); appsCard = Card(appContent); lists.Children.Add(appsCard);
        var networkContent = new StackPanel { Spacing = 8 }; networkContent.Children.Add(HeadingLink(Loc.Get("Design_Destinations"), "Network")); networkContent.Children.Add(connectionCount); networkContent.Children.Add(destinations);
        noConnections.Text = Loc.Get("Design_NoConnections"); networkContent.Children.Add(noConnections); networkCard = Card(networkContent); lists.Children.Add(networkCard); root.Children.Add(lists);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SizeChanged += (_, e) => Layout(e.NewSize.Width);
        Loaded += (_, _) => { App.Monitor.Updated -= Update; App.Monitor.Updated += Update; if (App.Monitor.Latest is { } snapshot) Update(snapshot); };
        Unloaded += (_, _) => App.Monitor.Updated -= Update;
    }



    private void Layout(double width)
    {
        int mode = width < 760 ? 0 : width < 950 ? 1 : 2;
        if (layoutMode == mode) return; layoutMode = mode;
        metrics.ColumnDefinitions.Clear(); metrics.RowDefinitions.Clear(); int columns = mode == 0 ? 2 : 4;
        for (int i = 0; i < columns; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4 / columns; i++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < metrics.Children.Count; i++) { Grid.SetColumn((FrameworkElement)metrics.Children[i], i % columns); Grid.SetRow((FrameworkElement)metrics.Children[i], i / columns); }
        ArrangePair(charts, memoryCard, mode == 0, 1.8); ArrangePair(lists, networkCard, mode < 2, 1.2);
    }
    private static void ArrangePair(Grid grid, FrameworkElement second, bool stacked, double ratio)
    {
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stacked ? 1 : ratio, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (stacked) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); else grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(second, stacked ? 1 : 0); Grid.SetColumn(second, stacked ? 0 : 1);
    }
    private void Update(MonitorSnapshot snapshot)
    {
        var system = snapshot.System;
        summary.Text = Loc.F("Design_OverviewSummary", Environment.MachineName, Format.Count(system.ProcessCount)); live.Text = Loc.Get(App.Monitor.Paused ? "Status_Paused" : "Design_Live");
        uptime.Text = Loc.F("Design_Uptime", $"{(int)system.Uptime.TotalHours:00}:{system.Uptime.Minutes:00}:{system.Uptime.Seconds:00}");
        cpu.Update(system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "–", Loc.F("Design_LogicalCores", Environment.ProcessorCount), App.Monitor.CpuHistory, 100);
        memory.Update(Format.WholePercent(system.Memory.LoadPercent), Loc.F("Design_OfInstalled", Format.Gigabytes(system.Memory.Total)), App.Monitor.MemoryHistory, 100);
        down.Update(system.NetworkSampled ? Format.NetworkRate(system.NetworkReceiveBytesPerSecond) : "–", Loc.Get("Design_AllAdapters"), App.Monitor.ReceiveHistory, Math.Max(1024, App.Monitor.ReceiveHistory.Max()));
        up.Update(system.NetworkSampled ? Format.NetworkRate(system.NetworkSendBytesPerSecond) : "–", Loc.Get("Design_AllAdapters"), App.Monitor.SendHistory, Math.Max(1024, App.Monitor.SendHistory.Max()));
        activity.Update(App.Monitor.CpuHistory, 100, App.Monitor.MemoryHistory);
        memoryValue.Text = Format.Gigabytes(system.Memory.InUse); memoryDetail.Text = Loc.F("Design_MemoryTotal", Format.Gigabytes(system.Memory.Total)); memoryBar.Value = system.Memory.LoadPercent;
        availableValue.Text = Format.Gigabytes(system.Memory.Available); committedValue.Text = Format.Gigabytes(system.Memory.CommitTotal) + " / " + Format.Gigabytes(system.Memory.CommitLimit);
        processCount.Text = Loc.Get("Design_SortedMemory");
        var leaders = snapshot.Processes.Where(p => p.Pid > 0).GroupBy(p => p.Path.Length > 0 ? p.Path : p.ImageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Process = g.First(), Memory = g.Sum(p => p.PrivateWorkingSet), Cpu = g.Sum(p => p.CpuPercent), Count = g.Count() }).OrderByDescending(g => g.Memory).Take(5);
        int appIndex = 0;
        foreach (var app in leaders)
        {
            if (appIndex == apps.Children.Count) apps.Children.Add(CreateAppRow());
            var button = (Button)apps.Children[appIndex++];
            var row = (Grid)button.Content;
            ((Image)row.Children[0]).Source = IconCache.Get(app.Process.Path);
            ((TextBlock)row.Children[1]).Text = app.Process.DisplayName + (app.Count > 1 ? $" ({app.Count})" : "");
            ((TextBlock)row.Children[2]).Text = Format.Percent(app.Cpu);
            ((TextBlock)row.Children[3]).Text = Format.Memory(app.Memory);
            button.Tag = app.Process;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Loc.Get("Design_InspectApp") + " " + app.Process.DisplayName);
        }
        while (apps.Children.Count > appIndex) apps.Children.RemoveAt(apps.Children.Count - 1);
        var established = snapshot.Connections.Where(c => c.Protocol == "TCP" && c.State == "Established").ToList();
        connectionCount.Text = Loc.F("Design_ConnectionSummary", Format.Count(established.Count), Format.Count(established.Select(c => c.Pid).Distinct().Count()));
        var names = snapshot.Processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().DisplayName);
        int destinationIndex = 0;
        foreach (var connection in established.OrderByDescending(c => c.ReceiveBytesPerSecond + c.SendBytesPerSecond).DistinctBy(c => (c.Pid, c.RemoteAddress, c.RemotePort)).Take(5))
        {
            if (destinationIndex == destinations.Children.Count) destinations.Children.Add(CreateDestinationRow());
            var button = (Button)destinations.Children[destinationIndex++];
            var row = (Grid)button.Content;
            var texts = (StackPanel)row.Children[1];
            ((TextBlock)texts.Children[0]).Text = connection.RemoteHost ?? connection.RemoteAddress;
            ((TextBlock)texts.Children[1]).Text = names.GetValueOrDefault(connection.Pid, "PID " + connection.Pid);
            ((TextBlock)row.Children[2]).Text = connection.Protocol + " :" + connection.RemotePort;
            button.Tag = connection.RemoteAddress;
            ToolTipService.SetToolTip(button, connection.RemoteAddress);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, connection.RemoteAddress);
        }
        while (destinations.Children.Count > destinationIndex) destinations.Children.RemoveAt(destinations.Children.Count - 1);
        noConnections.Visibility = established.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static TextBlock Text(string text, double size, bool strong = false, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center }; block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"); return block;
    }
    private static Button CreateAppRow()
    {
        var row = new Grid { ColumnSpacing = 12, Height = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.Children.Add(new Image { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center });
        for (int i = 1; i <= 3; i++)
        {
            var block = Text("", i == 1 ? 13 : 12, strong: i == 1, secondary: i == 2);
            if (i > 1) block.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(block, i); row.Children.Add(block);
        }
        Button? button = null;
        button = RowButton(row, () => { if (button!.Tag is ProcessSample process) App.Window.ShowProcess(process); }, "");
        return button;
    }
    private static Button CreateDestinationRow()
    {
        var row = new Grid { Height = 44, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Dot("BrandBrush"));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        texts.Children.Add(Text("", 12)); texts.Children.Add(Text("", 11, secondary: true)); Grid.SetColumn(texts, 1); row.Children.Add(texts);
        var port = Text("", 10, secondary: true); Grid.SetColumn(port, 2); row.Children.Add(port);
        Button? button = null;
        button = RowButton(row, () => { App.Window.SetSearch((string?)button!.Tag ?? ""); App.Window.NavigatePage("Network"); }, "");
        return button;
    }
    private static Ellipse Dot(string brush) { var dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center }; dot.SetResourceReference(Shape.FillProperty, brush); return dot; }
    private static Border Card(UIElement content) { var border = new Border { Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) }; border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeBrush"); return border; }
    private static Grid Heading(string title, string hint) { var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(Text(title, 14, strong: true)); var secondary = Text(hint, 11, secondary: true); Grid.SetColumn(secondary, 1); grid.Children.Add(secondary); return grid; }
    private static Grid HeadingLink(string title, string page)
    {
        var grid = Heading(title, ""); var button = new Button { Content = new FontIcon { Glyph = "\uE76C", FontSize = 12 }, MinWidth = 0, Padding = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) }; button.SetResourceReference(Control.ForegroundProperty, "BrandBrush");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Loc.Get("Design_Open") + " " + title); button.Click += (_, _) => { App.Window.SetSearch(""); App.Window.NavigatePage(page); }; Grid.SetColumn(button, 1); grid.Children.Add(button); return grid;
    }
    private static UIElement Legend(string label, string brush) { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; panel.Children.Add(Dot(brush)); panel.Children.Add(Text(label, 11, secondary: true)); return panel; }
    private static Grid Fact(string label, TextBlock value) { var row = new Grid { Margin = new Thickness(0, 2, 0, 2) }; row.Children.Add(Text(label, 12, secondary: true)); value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); return row; }
    private static Grid ColumnLabels(string name, string cpu, string memory)
    {
        var row = new Grid { Margin = new Thickness(8, 5, 8, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.Children.Add(Text(name, 10, secondary: true));
        var c = Text(cpu, 10, secondary: true); c.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(c, 1); row.Children.Add(c); var m = Text(memory, 10, secondary: true); m.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(m, 2); row.Children.Add(m); return row;
    }
    private static Button RowButton(UIElement content, Action click, string name)
    {
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new CornerRadius(6) }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name); button.Click += (_, _) => click(); return button;
    }
}
