using BetterTaskManager.Core.Monitoring;
using BetterTaskManager.Fluent.Services;
using BetterTaskManager.Fluent.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BetterTaskManager.Fluent.Views;

/// <summary>
/// Disk activity like Resource Monitor's Disk tab. The per-file list needs a kernel trace, which runs only while that
/// list is on screen in a window with administrator rights.
/// </summary>
public sealed partial class DiskPage : Page
{
    private readonly ColumnReorder processReorder, fileReorder;
    private bool visible;

    public DiskPage()
    {
        ViewModel = new DiskViewModel(App.Monitor, App.Settings);
        InitializeComponent();
        _ = new ColumnReorder(StorageHeader, ViewModel.StorageLayout);
        processReorder = new ColumnReorder(ProcessHeader, ViewModel.ProcessLayout);
        fileReorder = new ColumnReorder(FileHeader, ViewModel.FileLayout);
        UpdateSortIndicators();
    }

    public DiskViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        visible = true;
        App.Monitor.Updated += OnUpdated;
        App.Monitor.SearchChanged += Refresh;
        PauseButton.IsChecked = App.Monitor.Paused;
        UpdateTrace();
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        visible = false;
        App.Monitor.Updated -= OnUpdated;
        App.Monitor.SearchChanged -= Refresh;
        UpdateTrace();
    }

    private void OnUpdated(MonitorSnapshot snapshot) => Refresh();

    private void Refresh()
    {
        object? selected = ViewModel.FilesMode ? (FileList.SelectedItem as DiskFileSlot)?.Row : (ProcessList.SelectedItem as DiskProcessSlot)?.Row;
        ViewModel.Refresh();
        // Rows are reused slots: keep the selection on the same process or file after the list re-sorted.
        if (ViewModel.FilesMode && selected is DiskFileRow file)
            FileList.SelectedItem = ViewModel.Files.FirstOrDefault(slot => slot.Row is { } row && row.Pid == file.Pid && row.File == file.File);
        else if (!ViewModel.FilesMode && selected is DiskProcessRow process)
            ProcessList.SelectedItem = ViewModel.Processes.FirstOrDefault(slot => slot.Row is { } row && row.Pid == process.Pid && row.CreateTime == process.CreateTime);
        UpdateCommands();
    }

    /// <summary>Runs the per-file trace only while its list is visible, so its kernel buffers exist only then.</summary>
    private void UpdateTrace()
    {
        bool wanted = visible && ViewModel.FilesMode && App.Monitor.IsElevated;
        var files = App.Monitor.DiskFiles;
        if (wanted && !files.IsRunning) _ = Task.Run(files.Start);
        else if (!wanted && files.IsRunning) _ = Task.Run(files.Stop);
    }

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ViewModel is null) return;
        ViewModel.FilesMode = ReferenceEquals(ModeSelector.SelectedItem, FilesMode);
        ProcessHeader.Visibility = ProcessList.Visibility = ViewModel.FilesMode ? Visibility.Collapsed : Visibility.Visible;
        FileHeader.Visibility = FileList.Visibility = ViewModel.FilesMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrace();
        UpdateSortIndicators();
        Refresh();
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if ((ViewModel.FilesMode ? fileReorder : processReorder).SuppressClick) return;
        if (sender is not FrameworkElement { Tag: string column }) return;
        ViewModel.Sort(column);
        UpdateSortIndicators();
    }

    private void UpdateSortIndicators()
    {
        var process = new Dictionary<string, FontIcon>
        {
            [DiskViewModel.SortName] = ProcSortName, [DiskViewModel.SortPid] = ProcSortPid, [DiskViewModel.SortRead] = ProcSortRead,
            [DiskViewModel.SortWrite] = ProcSortWrite, [DiskViewModel.SortTotal] = ProcSortTotal
        };
        foreach (var (column, icon) in process)
        {
            icon.Visibility = column == ViewModel.ProcessSortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.ProcessSortDescending ? "" : "";
        }
        var file = new Dictionary<string, FontIcon>
        {
            [DiskViewModel.SortName] = FileSortName, [DiskViewModel.SortPid] = FileSortPid, [DiskViewModel.SortFile] = FileSortFile,
            [DiskViewModel.SortRead] = FileSortRead, [DiskViewModel.SortWrite] = FileSortWrite, [DiskViewModel.SortTotal] = FileSortTotal,
            [DiskViewModel.SortPriority] = FileSortPriority, [DiskViewModel.SortResponse] = FileSortResponse
        };
        foreach (var (column, icon) in file)
        {
            icon.Visibility = column == ViewModel.FileSortColumn ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ViewModel.FileSortDescending ? "" : "";
        }
    }

    private void StorageSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.StorageLayout.Resize(column, e.HorizontalChange);
    }

    private void ProcessSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.ProcessLayout.Resize(column, e.HorizontalChange);
    }

    private void FileSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string column }) ViewModel.FileLayout.Resize(column, e.HorizontalChange);
    }

    /// <summary>The selected file if it still exists, otherwise the program that did the I/O.</summary>
    private string SelectedPath()
    {
        if (ViewModel.FilesMode && FileList.SelectedItem is DiskFileSlot { Row: { } file })
            return File.Exists(file.File) ? file.File : file.ProcessPath;
        return (ProcessList.SelectedItem as DiskProcessSlot)?.Row?.Path ?? "";
    }

    private void UpdateCommands() => OpenLocationButton.IsEnabled = SelectedPath().Length > 0;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ProcessActions.OpenFileLocation(SelectedPath());

    private void OpenLocation_Click(object sender, RoutedEventArgs e) => ProcessActions.OpenFileLocation(SelectedPath());

    private void Pause_Click(object sender, RoutedEventArgs e) => App.Monitor.Paused = PauseButton.IsChecked == true;

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => App.RestartElevated();
}
