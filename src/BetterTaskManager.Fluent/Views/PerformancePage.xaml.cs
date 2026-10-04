using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Core.Native;
using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

public sealed partial class PerformancePage : Page
{
    private sealed record Segment(string Label, string Description, string BrushKey, Func<MemoryBreakdown, long> Value, bool InUse);

    private static readonly Segment[] Segments =
    [
        new(Loc.Get("Mem_Apps"), Loc.Get("Mem_AppsDesc"), "MemoryAppsBrush", m => m.ProcessPrivate, true),
        new(Loc.Get("Mem_Kernel"), Loc.Get("Mem_KernelDesc"), "MemoryKernelBrush", m => m.KernelPools, true),
        new(Loc.Get("Mem_Cache"), Loc.Get("Mem_CacheDesc"), "MemoryCacheBrush", m => m.FileCache, true),
        new(Loc.Get("Mem_Drivers"), Loc.Get("Mem_DriversDesc"), "MemoryDriversBrush", m => m.Drivers, true),
        new(Loc.Get("Mem_Shared"), Loc.Get("Mem_SharedDesc"), "MemoryOtherBrush", m => m.Unattributed, true),
        new(Loc.Get("Mem_Modified"), Loc.Get("Mem_ModifiedDesc"), "MemoryModifiedBrush", m => m.Modified, false),
        new(Loc.Get("Mem_Standby"), Loc.Get("Mem_StandbyDesc"), "MemoryStandbyBrush", m => m.Standby, false),
        new(Loc.Get("Mem_Free"), Loc.Get("Mem_FreeDesc"), "MemoryFreeBrush", m => m.Free, false)
    ];

    private readonly List<(Segment Segment, TextBlock Value, ColumnDefinition Column)> parts = new();

    public PerformancePage()
    {
        InitializeComponent();
        BuildMemoryBreakdown();
        if (!App.Monitor.IsElevated)
        {
            foreach (MenuFlyoutItem item in new[] { StandbyItem, SystemItem })
            {
                item.IsEnabled = false;
                item.Text += Loc.Get("Cleanup_NeedsAdmin");
            }
        }
    }

    private sealed record CleanupAction(string Title, string Explanation, string Button);

    private static readonly Dictionary<string, CleanupAction> CleanupActions = new()
    {
        ["Trim"] = new(Loc.Get("Cleanup_TrimTitle"), Loc.Get("Cleanup_TrimText"), Loc.Get("Cleanup_TrimButton")),
        ["Standby"] = new(Loc.Get("Cleanup_StandbyTitle"), Loc.Get("Cleanup_StandbyText"), Loc.Get("Cleanup_StandbyButton")),
        ["System"] = new(Loc.Get("Cleanup_SystemTitle"), Loc.Get("Cleanup_SystemText"), Loc.Get("Cleanup_SystemButton"))
    };

    /// <summary>One flyout item: trim, clear standby, or empty working sets, each with its own confirmation.</summary>
    private async void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string kind } || !CleanupActions.TryGetValue(kind, out CleanupAction? action)) return;
        await RunCleanupsAsync(new[] { kind }, action.Title, action.Explanation + "\n\n" + Loc.Get("Cleanup_Note"), action.Button);
    }

    /// <summary>The primary button runs every cleanup available at this rights level in one click.</summary>
    private async void CleanupAll_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        string[] kinds = App.Monitor.IsElevated ? new[] { "Trim", "Standby", "System" } : new[] { "Trim" };
        string body = (App.Monitor.IsElevated ? Loc.Get("Cleanup_AllTextElevated") : Loc.Get("Cleanup_AllTextStandard")) + "\n\n" + Loc.Get("Cleanup_Note");
        await RunCleanupsAsync(kinds, Loc.Get("Cleanup_AllTitle"), body, Loc.Get("Cleanup_AllButton"));
    }

    private async Task RunCleanupsAsync(IReadOnlyList<string> kinds, string title, string body, string button)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = button,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        CleanupButton.IsEnabled = false;
        MemoryBreakdown? before = App.Monitor.Latest?.System.Memory;
        var messages = new List<string>();
        bool allOk = true;
        foreach (string kind in kinds)
        {
            (bool ok, string message) = await RunOneAsync(kind);
            allOk &= ok;
            messages.Add(message);
        }

        // Let the monitor take a fresh sample so the effect is measured, not guessed.
        App.Monitor.RequestRefresh();
        await Task.Delay(1500);
        MemoryBreakdown? after = App.Monitor.Latest?.System.Memory;
        string combined = string.Join("\n", messages);
        if (allOk && before is not null && after is not null)
        {
            combined += Loc.F("Cleanup_Delta", Format.Gigabytes(before.InUse), Format.Gigabytes(after.InUse),
                Format.Gigabytes(before.Standby), Format.Gigabytes(after.Standby),
                Format.Gigabytes(before.Free), Format.Gigabytes(after.Free));
        }
        CleanupBar.Severity = allOk ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        CleanupBar.Title = allOk ? Loc.Get("Cleanup_Done") : Loc.Get("Cleanup_NotDone");
        CleanupBar.Message = combined;
        CleanupBar.IsOpen = true;
        CleanupButton.IsEnabled = true;
    }

    private static async Task<(bool Succeeded, string Message)> RunOneAsync(string kind)
    {
        switch (kind)
        {
            case "Trim":
                return await Task.Run(() =>
                {
                    TrimResult trim = MemoryCleanup.TrimAllWorkingSets(Environment.ProcessId);
                    string refused = trim.Denied == 0 ? "" : App.Monitor.IsElevated
                        ? Loc.F("Cleanup_ProtectedRefused", trim.Denied)
                        : Loc.F("Cleanup_NeedAdminCount", trim.Denied);
                    return (true, Loc.F("Cleanup_Trimmed", Format.Count(trim.Trimmed)) + refused);
                });
            case "Standby":
                return ToTuple(await Task.Run(MemoryCleanup.PurgeStandbyList), "Cleanup_StandbyDone");
            default:
                return ToTuple(await Task.Run(MemoryCleanup.EmptySystemWorkingSets), "Cleanup_SystemDone");
        }
    }

    private static (bool, string) ToTuple(CleanupResult result, string doneKey) =>
        (result.Succeeded, result.Succeeded ? Loc.Get(doneKey) : Loc.CleanupFailure(result));

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Monitor.Updated += OnUpdated;
        if (App.Monitor.Latest is { } snapshot) OnUpdated(snapshot);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => App.Monitor.Updated -= OnUpdated;

    private void BuildMemoryBreakdown()
    {
        for (int index = 0; index < Segments.Length; index++)
        {
            Segment segment = Segments[index];
            var brush = (Brush)Application.Current.Resources[segment.BrushKey];

            var column = new ColumnDefinition { Width = new GridLength(0) };
            MemoryBar.ColumnDefinitions.Add(column);
            var block = new Border { Background = brush };
            ToolTipService.SetToolTip(block, segment.Label);
            Grid.SetColumn(block, index);
            MemoryBar.Children.Add(block);

            var value = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], HorizontalAlignment = HorizontalAlignment.Right };
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = brush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = segment.Label });
            text.Children.Add(new TextBlock { Text = segment.Description, Style = (Style)Application.Current.Resources["MutedTextStyle"], TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(text);
            row.Children.Add(value);

            (segment.InUse ? InUseLegend : AvailableLegend).Children.Add(row);
            parts.Add((segment, value, column));
        }

        InUseLegend.Children.Insert(0, new TextBlock { Text = Loc.Get("Mem_InUse"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Name = "InUseHeader" });
        AvailableLegend.Children.Insert(0, new TextBlock { Text = Loc.Get("Mem_NotInUse"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Name = "AvailableHeader" });
    }

    private void OnUpdated(MonitorSnapshot snapshot)
    {
        SystemSample system = snapshot.System;
        MemoryBreakdown memory = system.Memory;
        MonitorHost monitor = App.Monitor;

        SummaryText.Text = Loc.F("Perf_Updated", snapshot.Timestamp.ToString("T"), snapshot.CollectionTime.TotalMilliseconds.ToString("0"));

        CpuValue.Text = system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "…";
        CpuChart.Update(monitor.CpuHistory, 100);
        CpuDetail.Text = Loc.F("Perf_CpuDetail", Format.Count(system.ProcessCount), Format.Count(system.ThreadCount),
            Format.Count(system.HandleCount), Environment.ProcessorCount, Format.Duration(system.Uptime));

        MemoryValue.Text = $"{Format.Gigabytes(memory.InUse)} / {Format.Gigabytes(memory.Total)} ({Format.WholePercent(memory.LoadPercent)})";
        MemoryChart.Update(monitor.MemoryHistory, 100);
        MemoryDetail.Text = Loc.F("Perf_MemoryDetail", Format.Gigabytes(memory.Available), Format.Gigabytes(memory.Standby + memory.Modified),
            Format.Gigabytes(memory.CommitTotal), Format.Gigabytes(memory.CommitLimit));

        NetworkValue.Text = system.NetworkSampled
            ? $"↓ {Format.NetworkRate(system.NetworkReceiveBytesPerSecond)}   ↑ {Format.NetworkRate(system.NetworkSendBytesPerSecond)}"
            : "…";
        double networkMax = Math.Max(Math.Max(monitor.ReceiveHistory.Max(), monitor.SendHistory.Max()) * 1.15, 125_000);
        NetworkChart.Update(monitor.ReceiveHistory, networkMax, monitor.SendHistory);
        NetworkDetail.Text = Loc.F("Perf_NetworkDetail", Format.NetworkRate(networkMax), Format.Count(snapshot.Connections.Count));

        foreach (var (segment, value, column) in parts)
        {
            long bytes = segment.Value(memory);
            value.Text = Format.Gigabytes(bytes);
            column.Width = new GridLength(Math.Max(0, bytes), GridUnitType.Star);
        }
        if (InUseLegend.Children[0] is TextBlock inUseHeader) inUseHeader.Text = Loc.F("Mem_InUseTotal", Format.Gigabytes(memory.InUse));
        if (AvailableLegend.Children[0] is TextBlock availableHeader)
        {
            availableHeader.Text = Loc.F("Mem_NotInUseTotal", Format.Gigabytes(memory.Modified + memory.Standby + memory.Free));
        }
    }
}
