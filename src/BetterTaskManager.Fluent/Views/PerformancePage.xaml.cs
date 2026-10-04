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
        new("Apps and processes", "Memory private to each process. Task Manager's per-app Memory column adds up to this.", "MemoryAppsBrush", m => m.ProcessPrivate, true),
        new("Windows kernel", "Kernel and driver data structures held in RAM (paged pool in RAM plus non-paged pool).", "MemoryKernelBrush", m => m.KernelPools, true),
        new("File cache in use", "File data Windows currently has mapped for reading and writing.", "MemoryCacheBrush", m => m.FileCache, true),
        new("Driver code", "Loaded kernel-mode driver code.", "MemoryDriversBrush", m => m.Drivers, true),
        new("Shared and other", "Shared program code and mapped files (counted once), page tables, and memory drivers lock directly, for example GPU and VM drivers. Sysinternals RAMMap can split this further.", "MemoryOtherBrush", m => m.Unattributed, true),
        new("Modified", "Changed data waiting to be written to disk. Becomes available afterwards.", "MemoryModifiedBrush", m => m.Modified, false),
        new("Standby cache", "Recently used data kept for speed. Windows hands it to apps immediately, so it counts as available.", "MemoryStandbyBrush", m => m.Standby, false),
        new("Free", "Unused memory.", "MemoryFreeBrush", m => m.Free, false)
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
                item.Text += " (needs administrator)";
            }
        }
    }

    private sealed record CleanupAction(string Title, string Explanation, string Button);

    private static readonly Dictionary<string, CleanupAction> CleanupActions = new()
    {
        ["Trim"] = new("Trim app memory?",
            "Asks every app this account can reach to give back the memory it is not actively using. Nax-TaskManager itself is skipped. " +
            "\"In use\" drops, but the pages only move to the standby or modified list, and apps read them back in as they need them, which can make them briefly slower.",
            "Trim"),
        ["Standby"] = new("Clear the standby cache?",
            "Discards recently used file and program data that Windows keeps in RAM for speed. Free memory goes up, but the next launches and file reads come from disk again. " +
            "Standby memory already counts as available, so this rarely helps an app that is short of memory.",
            "Clear"),
        ["System"] = new("Empty all working sets?",
            "Trims every process at once, including Windows services and the kernel's system working set. Expect stutter for a few seconds while everything pages back in.",
            "Empty")
    };

    private async void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string kind } || !CleanupActions.TryGetValue(kind, out CleanupAction? action)) return;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action.Title,
            Content = new TextBlock
            {
                Text = action.Explanation + "\n\nWindows uses spare RAM as cache on purpose. These are troubleshooting tools, not routine optimisation.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = action.Button,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        CleanupButton.IsEnabled = false;
        MemoryBreakdown? before = App.Monitor.Latest?.System.Memory;
        (bool succeeded, string message) = kind switch
        {
            "Trim" => await Task.Run(() =>
            {
                TrimResult trim = MemoryCleanup.TrimAllWorkingSets(Environment.ProcessId);
                string refused = trim.Denied == 0 ? "" : App.Monitor.IsElevated
                    ? $" {trim.Denied} protected processes refused."
                    : $" {trim.Denied} processes need administrator rights.";
                return (true, $"Trimmed {Format.Count(trim.Trimmed)} processes.{refused}");
            }),
            "Standby" => ToTuple(await Task.Run(MemoryCleanup.PurgeStandbyList)),
            _ => ToTuple(await Task.Run(MemoryCleanup.EmptySystemWorkingSets))
        };

        // Let the monitor take a fresh sample so the effect is measured, not guessed.
        App.Monitor.RequestRefresh();
        await Task.Delay(1500);
        MemoryBreakdown? after = App.Monitor.Latest?.System.Memory;
        if (succeeded && before is not null && after is not null)
        {
            message += $" In use {Format.Gigabytes(before.InUse)} → {Format.Gigabytes(after.InUse)}, " +
                $"standby {Format.Gigabytes(before.Standby)} → {Format.Gigabytes(after.Standby)}, " +
                $"free {Format.Gigabytes(before.Free)} → {Format.Gigabytes(after.Free)}.";
        }
        CleanupBar.Severity = succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        CleanupBar.Title = succeeded ? "Done" : "Not done";
        CleanupBar.Message = message;
        CleanupBar.IsOpen = true;
        CleanupButton.IsEnabled = true;

        static (bool, string) ToTuple(CleanupResult result) => (result.Succeeded, result.Message);
    }

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

        InUseLegend.Children.Insert(0, new TextBlock { Text = "In use", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Name = "InUseHeader" });
        AvailableLegend.Children.Insert(0, new TextBlock { Text = "Not in use", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Name = "AvailableHeader" });
    }

    private void OnUpdated(MonitorSnapshot snapshot)
    {
        SystemSample system = snapshot.System;
        MemoryBreakdown memory = system.Memory;
        MonitorHost monitor = App.Monitor;

        SummaryText.Text = $"Updated {snapshot.Timestamp:T} · collected in {snapshot.CollectionTime.TotalMilliseconds:0} ms";

        CpuValue.Text = system.CpuSampled ? Format.WholePercent(system.CpuPercent) : "…";
        CpuChart.Update(monitor.CpuHistory, 100);
        CpuDetail.Text = $"{Format.Count(system.ProcessCount)} processes · {Format.Count(system.ThreadCount)} threads · " +
            $"{Format.Count(system.HandleCount)} handles · {Environment.ProcessorCount} logical processors · up {Format.Duration(system.Uptime)}";

        MemoryValue.Text = $"{Format.Gigabytes(memory.InUse)} / {Format.Gigabytes(memory.Total)} ({Format.WholePercent(memory.LoadPercent)})";
        MemoryChart.Update(monitor.MemoryHistory, 100);
        MemoryDetail.Text = $"Available {Format.Gigabytes(memory.Available)} · Cached {Format.Gigabytes(memory.Standby + memory.Modified)} · " +
            $"Committed {Format.Gigabytes(memory.CommitTotal)} / {Format.Gigabytes(memory.CommitLimit)}";

        NetworkValue.Text = system.NetworkSampled
            ? $"↓ {Format.NetworkRate(system.NetworkReceiveBytesPerSecond)}   ↑ {Format.NetworkRate(system.NetworkSendBytesPerSecond)}"
            : "…";
        double networkMax = Math.Max(Math.Max(monitor.ReceiveHistory.Max(), monitor.SendHistory.Max()) * 1.15, 125_000);
        NetworkChart.Update(monitor.ReceiveHistory, networkMax, monitor.SendHistory);
        NetworkDetail.Text = $"Scale {Format.NetworkRate(networkMax)} · {Format.Count(snapshot.Connections.Count)} open connections · " +
            "receive (green) and send (amber), all active adapters";

        foreach (var (segment, value, column) in parts)
        {
            long bytes = segment.Value(memory);
            value.Text = Format.Gigabytes(bytes);
            column.Width = new GridLength(Math.Max(0, bytes), GridUnitType.Star);
        }
        if (InUseLegend.Children[0] is TextBlock inUseHeader) inUseHeader.Text = $"In use · {Format.Gigabytes(memory.InUse)}";
        if (AvailableLegend.Children[0] is TextBlock availableHeader)
        {
            availableHeader.Text = $"Not in use · {Format.Gigabytes(memory.Modified + memory.Standby + memory.Free)}";
        }
    }
}
