using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Controls;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.Views;

/// <summary>Physical RAM and its measured owners, kept separate from virtual commit.</summary>
public sealed partial class MemoryPage : Page
{
    private readonly Grid metrics = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly Grid lower = new() { ColumnSpacing = 18, RowSpacing = 18 };
    private readonly Grid capacity = new() { Height = 18, ColumnSpacing = 3, CornerRadius = new CornerRadius(5) };
    private readonly StatTile used = new(), available = new(), cached = new(), commit = new();
    private readonly TrendChart history = new() { Height = 100 };
    private readonly TextBlock stamp = Text("", 12, secondary: true), total = Text("", 14, true);
    private readonly TextBlock reconciliation = Text("", 12, secondary: true), availableNote = Text("", 12, secondary: true);
    private readonly TextBlock usedHeader = Text("", 14, true), readyHeader = Text("", 14, true);
    private readonly StackPanel accounting = new() { Spacing = 12 }, applications = new();
    private readonly List<TextBlock> values = new();
    private readonly Border appsCard;
    private readonly HistoryBuffer availableHistory = new(60), cachedHistory = new(60), commitHistory = new(60);
    private DateTime sampledAt;
    private int layoutMode = -1;

    public MemoryPage()
    {
        InitializeComponent();
        var root = new StackPanel { Spacing = 18, Padding = new Thickness(28, 20, 28, 24) };
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(Text(Loc.Get("Memory_Title"), 32, true));
        header.Children.Add(Text(Loc.Get("Memory_Subtitle"), 13, secondary: true));
        root.Children.Add(header);
        StatTile[] tiles = [used, available, cached, commit];
        string[] labels = ["Mem_InUse", "Design_Available", "Memory_Cached", "Design_Committed"];
        string[] brushes = ["ChartMemoryBrush", "BrandBrush", "ChartReceiveBrush", "ChartCpuBrush"];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Caption = Loc.Get(labels[i]);
            tiles[i].SetResourceReference(StatTile.StrokeProperty, brushes[i]);
            metrics.Children.Add(tiles[i]);
        }
        root.Children.Add(metrics);

        var composition = new StackPanel { Spacing = 12 };
        composition.Children.Add(Heading("Memory_Physical", total));
        composition.Children.Add(Text(Loc.Get("Memory_PhysicalNote"), 12, secondary: true));
        string[] capacityBrushes = ["ChartMemoryBrush", "MemoryModifiedBrush", "BrandBrush"];
        string[] capacityLabels = ["Mem_InUse", "Mem_Modified", "Design_Available"];
        for (int i = 0; i < 3; i++)
        {
            capacity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var block = new Border(); block.SetResourceReference(Border.BackgroundProperty, capacityBrushes[i]);
            ToolTipService.SetToolTip(block, Loc.Get(capacityLabels[i]));
            Grid.SetColumn(block, i); capacity.Children.Add(block);
        }
        composition.Children.Add(capacity);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        for (int i = 0; i < 3; i++)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4) };
            dot.SetResourceReference(Border.BackgroundProperty, capacityBrushes[i]);
            item.Children.Add(dot); item.Children.Add(Text(Loc.Get(capacityLabels[i]), 12, secondary: true)); legend.Children.Add(item);
        }
        composition.Children.Add(legend);
        history.SetResourceReference(TrendChart.StrokeProperty, "ChartMemoryBrush");
        composition.Children.Add(history);
        composition.Children.Add(stamp);
        root.Children.Add(Card(composition));

        accounting.Children.Add(Text(Loc.Get("Memory_Accounting"), 16, true));
        string[] names = ["Mem_Apps", "Memory_PagedPool", "Memory_NonpagedPool", "Mem_Cache", "Mem_Drivers", "Memory_Residual", "Mem_Modified", "Mem_Standby", "Mem_Free"];
        foreach (string name in names)
        {
            // Two groups: what is really occupied, and what Windows hands to programs the moment they ask.
            if (name == "Mem_Apps") accounting.Children.Add(usedHeader);
            if (name == "Mem_Modified")
            {
                usedHeader.Margin = new Thickness(0, 4, 0, 0);
                readyHeader.Margin = new Thickness(0, 10, 0, 0);
                accounting.Children.Add(readyHeader);
            }
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(Loc.Get(name), 13, secondary: true));
            var value = Text("", 13, true); Grid.SetColumn(value, 1); row.Children.Add(value); values.Add(value);
            accounting.Children.Add(row);
        }
        accounting.Children.Add(Text(Loc.Get("Memory_FreeNote"), 12, secondary: true));
        accounting.Children.Add(reconciliation); accounting.Children.Add(availableNote);
        lower.Children.Add(Card(accounting));

        var leaders = new StackPanel { Spacing = 10 };
        var showProcesses = new HyperlinkButton { Content = Loc.Get("Memory_AllProcesses"), Padding = new Thickness(0) };
        showProcesses.Click += (_, _) => { App.Window.SetSearch(""); App.Window.NavigatePage("Processes"); };
        leaders.Children.Add(Heading("Memory_AppsTitle", showProcesses));
        leaders.Children.Add(Text(Loc.Get("Memory_AppsNote"), 12, secondary: true));
        leaders.Children.Add(applications);
        appsCard = Card(leaders); lower.Children.Add(appsCard); root.Children.Add(lower);
        var explanation = new StackPanel { Spacing = 6 };
        explanation.Children.Add(Text(Loc.Get("Memory_Why"), 14, true));
        explanation.Children.Add(Text(Loc.Get("Memory_WhyText"), 12, secondary: true));
        root.Children.Add(Card(explanation));
        Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Loaded += (_, _) => { App.Monitor.Updated -= Update; App.Monitor.Updated += Update; if (App.Monitor.Latest is { } snapshot) Update(snapshot); };
        Unloaded += (_, _) => App.Monitor.Updated -= Update;
    }

    private void Arrange(double width)
    {
        int mode = width < 950 ? 0 : 1;
        if (layoutMode == mode) return;
        layoutMode = mode;
        metrics.ColumnDefinitions.Clear(); metrics.RowDefinitions.Clear();
        int columns = mode == 0 ? 2 : 4;
        for (int i = 0; i < columns; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4 / columns; i++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 4; i++) { Grid.SetColumn((FrameworkElement)metrics.Children[i], i % columns); Grid.SetRow((FrameworkElement)metrics.Children[i], i / columns); }
        lower.ColumnDefinitions.Clear(); lower.RowDefinitions.Clear();
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (mode == 0) lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        else lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(appsCard, mode == 0 ? 0 : 1); Grid.SetRow(appsCard, mode == 0 ? 1 : 0);
    }

    private void Update(MonitorSnapshot snapshot)
    {
        MemoryBreakdown m = snapshot.System.Memory;
        if (sampledAt != snapshot.Timestamp)
        {
            sampledAt = snapshot.Timestamp;
            availableHistory.Add(m.Available); cachedHistory.Add(m.Standby + m.Modified); commitHistory.Add(m.CommitTotal);
        }
        used.Update(Format.Gigabytes(m.InUse), Format.WholePercent(m.LoadPercent), App.Monitor.MemoryHistory, 100);
        available.Update(Format.Gigabytes(m.Available), Loc.Get("Memory_Ready"), availableHistory, Math.Max(1, m.Total));
        cached.Update(m.PageListsAvailable && m.ModifiedAvailable ? Format.Gigabytes(m.Standby + m.Modified) : Loc.Get("Memory_Unavailable"), Loc.Get("Memory_CachedNote"), cachedHistory, Math.Max(1, m.Total));
        commit.Update(Format.Gigabytes(m.CommitTotal), Loc.F("Memory_CommitLimit", Format.Gigabytes(m.CommitLimit)), commitHistory, Math.Max(1, m.CommitLimit));
        total.Text = Loc.F("Memory_Usable", Format.Gigabytes(m.Total));
        capacity.ColumnDefinitions[0].Width = new GridLength(m.InUse, GridUnitType.Star);
        capacity.ColumnDefinitions[1].Width = new GridLength(m.Modified, GridUnitType.Star);
        capacity.ColumnDefinitions[2].Width = new GridLength(m.Available, GridUnitType.Star);
        stamp.Text = Loc.F("Memory_Sampled", snapshot.Timestamp.ToString("T"), snapshot.CollectionTime.TotalMilliseconds.ToString("0"));
        history.Update(App.Monitor.MemoryHistory, 100);
        usedHeader.Text = Loc.F("Memory_UsedGroup", Format.Gigabytes(m.InUse));
        readyHeader.Text = Loc.F("Memory_ReadyGroup", Format.Gigabytes(m.Available + m.Modified));
        long[] bytes = [m.ProcessPrivate, m.PagedPoolResident, m.NonpagedPool, m.FileCache, m.Drivers, m.Unattributed, m.Modified, m.Standby, m.Free];
        for (int i = 0; i < bytes.Length; i++)
        {
            bool known = i == 0 || (i < 6 ? m.Detailed : i == 6 ? m.ModifiedAvailable : m.PageListsAvailable);
            values[i].Text = known ? Format.Memory(bytes[i]) : Loc.Get("Memory_Unavailable");
        }
        reconciliation.Text = !m.Detailed ? Loc.Get("Memory_Partial")
            : m.AttributionDifference < 0 ? Loc.F("Memory_Excess", Format.Memory(-m.AttributionDifference))
            : Loc.F("Memory_ResidualNote", Format.Memory(m.Unattributed));
        availableNote.Text = m.PageListsAvailable ? Loc.F("Memory_AvailableDifference", Format.Memory(Math.Abs(m.AvailableDifference))) : Loc.Get("Memory_PageListsMissing");
        if (!m.ModifiedAvailable) reconciliation.Text += " " + Loc.Get("Memory_ModifiedMissing");
        var groups = snapshot.Processes.Where(p => p.Pid > 0).GroupBy(ProcessTree.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Process = g.First(), Bytes = g.Sum(p => p.PrivateWorkingSet), Count = g.Count() })
            .OrderByDescending(g => g.Bytes).ThenBy(g => g.Process.DisplayName).Take(8).ToList();
        int index = 0;
        foreach (var group in groups)
        {
            if (applications.Children.Count == index) applications.Children.Add(AppRow());
            var button = (Button)applications.Children[index++];
            button.Tag = group.Process;
            var row = (Grid)button.Content;
            ((Image)row.Children[0]).Source = IconCache.Get(group.Process.Path);
            ((TextBlock)row.Children[1]).Text = Loc.AppName(group.Process.DisplayName) + (group.Count > 1 ? $" ({group.Count})" : "");
            ((TextBlock)row.Children[2]).Text = Format.Memory(group.Bytes);
            ToolTipService.SetToolTip(button, group.Process.Path);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Loc.Get("Design_InspectApp") + " " + group.Process.DisplayName);
        }
        while (applications.Children.Count > index) applications.Children.RemoveAt(applications.Children.Count - 1);
    }

    private static Button AppRow()
    {
        var row = new Grid { Height = 38, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Image { Width = 20, Height = 20 });
        var name = Text("", 13); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1); row.Children.Add(name);
        var value = Text("", 13, true); Grid.SetColumn(value, 2); row.Children.Add(value);
        var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        button.Click += (_, _) => { if (button.Tag is ProcessSample process) App.Window.ShowProcess(process); };
        return button;
    }
    private static TextBlock Text(string value, double size, bool strong = false, bool secondary = false)
    {
        var block = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"); return block;
    }
    private static Grid Heading(string label, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Text(Loc.Get(label), 16, true)); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private static Border Card(UIElement content)
    {
        var card = new Border { Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeBrush"); return card;
    }
}
