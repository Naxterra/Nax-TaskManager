using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterTaskManager
{
    public static class NativeMethods
    {
        public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int TOKEN_QUERY = 0x0008;

        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern IntPtr OpenProcess(int processAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder text, ref int size);

        [DllImport("dwmapi.dll", SetLastError=true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        [DllImport("uxtheme.dll", EntryPoint="#135", SetLastError=true)]
        public static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint="#136", SetLastError=true)]
        public static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("advapi32.dll", SetLastError=true)]
        public static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

        public static int PurgeStandbyList()
        {
            int command = 4;
            return NtSetSystemInformation(80, ref command, 4);
        }

        public static int EmptySystemWorkingSets()
        {
            int command = 2;
            return NtSetSystemInformation(80, ref command, 4);
        }
    }

    public sealed class ProcessDetails
    {
        public string Path = "";
        public string User = "";
        public bool PathResolved;
        public bool UserResolved;
        public long ProcessStartTimeUtcTicks;
    }

    public sealed class ProcessRow
    {
        public int Pid;
        public string Name = "";
        public string User = "";
        public double Cpu;
        public bool CpuSampleAvailable;
        public double PrivateMb;
        public double WorkingSetMb;
        public double PeakWorkingSetMb;
        public int Threads;
        public string Path = "";
        public long ProcessStartTimeUtcTicks;
    }

    public sealed class NetworkRow
    {
        public DateTime Timestamp;
        public string Process = "";
        public int Pid;
        public string User = "";
        public string Protocol = "";
        public string LocalAddress = "";
        public string LocalPort = "";
        public string RemoteAddress = "";
        public string RemotePort = "";
        public string State = "";
        public string Path = "";
    }

    public sealed class AppProfile
    {
        public string Name = "";
        public string Path = "";
        public string User = "";
        public readonly HashSet<int> Pids = new HashSet<int>();
        public int ConnectionCount;
        public double Cpu;
        public int CpuSampleCount;
        public double PrivateMb;
        public double RamMb;
    }

    internal sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    internal sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    internal enum GlobalShortcutCommand
    {
        None,
        Refresh,
        FocusFilter,
        ClearFilter,
        Export,
        ToggleLive,
        OpenApps,
        OpenProcesses,
        OpenNetwork,
        OpenHistory,
        OpenMemory,
        PreviousPage,
        NextPage
    }

    public sealed class MainForm : Form
    {
        private const int HistoryDisplayLimit = 100;
        private const string FirewallStatusBlocked = "BTM Blocked";
        private const string FirewallStatusNoBlock = "No BTM Block";

        private static class Theme
        {
            public static readonly Color Window = Color.FromArgb(18, 25, 36);
            public static readonly Color Surface = Color.FromArgb(24, 33, 47);
            public static readonly Color SurfaceAlt = Color.FromArgb(30, 42, 58);
            public static readonly Color SurfaceRaised = Color.FromArgb(39, 53, 72);
            public static readonly Color Border = Color.FromArgb(58, 76, 101);
            public static readonly Color BorderStrong = Color.FromArgb(80, 104, 137);
            public static readonly Color Text = Color.FromArgb(235, 242, 250);
            public static readonly Color MutedText = Color.FromArgb(163, 180, 201);
            public static readonly Color Accent = Color.FromArgb(63, 126, 178);
            public static readonly Color AccentHover = Color.FromArgb(77, 151, 207);
            public static readonly Color AccentSelected = Color.FromArgb(48, 105, 153);
            public static readonly Color Good = Color.FromArgb(91, 205, 160);
            public static readonly Color Warning = Color.FromArgb(235, 187, 92);
            public static readonly Color Danger = Color.FromArgb(239, 111, 117);
            public static readonly Color Info = Color.FromArgb(119, 185, 235);
        }

        private readonly bool isAdmin;
        private readonly DataGridView appGrid;
        private readonly DataGridView appConnectionsGrid;
        private readonly Button appRefreshButton;
        private readonly Button appBlockButton;
        private readonly Button appUnblockButton;
        private readonly Button appViewProcessesButton;
        private readonly Button appOpenFolderButton;
        private readonly Button appCopyPathButton;
        private readonly Label appFirewallCard;
        private readonly Label appFirewallDetailsLabel;
        private readonly TextBox appSearchBox;
        private readonly Label appTitleLabel;
        private readonly Label appMetaLabel;
        private readonly Label appConnectionCard;
        private readonly Label appMemoryCard;
        private readonly Label appRamCard;
        private readonly DataGridView processGrid;
        private readonly DataGridView networkGrid;
        private readonly Button refreshButton;
        private readonly Button killButton;
        private readonly Button trimSelectedButton;
        private readonly Button loadDetailsButton;
        private readonly Button processOpenFolderButton;
        private readonly Button processCopyPathButton;
        private readonly CheckBox liveMonitoringCheck;
        private readonly ComboBox refreshIntervalBox;
        private readonly Label liveStatusLabel;
        private readonly Button restartAdminButton;
        private readonly Label adminStatusLabel;
        private readonly Label statusLabel;
        private readonly Label processSummaryLabel;
        private readonly TextBox filterBox;
        private readonly Button networkRefreshButton;
        private readonly Button networkOpenFolderButton;
        private readonly Button networkCopyPathButton;
        private readonly Button blockButton;
        private readonly Button unblockButton;
        private readonly Label networkStatusLabel;
        private readonly Label bandwidthLabel;
        private readonly TextBox networkFilterBox;
        private readonly ListView historyList;
        private readonly Panel historyTab;
        private readonly Label historyNoteLabel;
        private readonly Button reloadHistoryButton;
        private readonly Button clearHistoryButton;
        private readonly Button historyPreviousButton;
        private readonly Button historyNextButton;
        private readonly TextBox historyFilterBox;
        private readonly Button trimAllButton;
        private readonly Button clearStandbyButton;
        private readonly Button emptySystemButton;
        private readonly Button memoryRefreshButton;
        private readonly Label memorySnapshotLabel;
        private readonly Label memoryCpuCard;
        private readonly Label memoryLoadCard;
        private readonly Label memoryUsedCard;
        private readonly Label memoryAvailableCard;
        private readonly Label memoryCommitCard;
        private readonly Label memoryCacheCard;
        private readonly Label memoryStatusLabel;
        private readonly Panel pageHost;
        private readonly FlowLayoutPanel navBar;
        private readonly FlowLayoutPanel appMetricCards;
        private readonly FlowLayoutPanel appActions;
        private readonly FlowLayoutPanel processToolbar;
        private readonly FlowLayoutPanel networkToolbar;
        private readonly FlowLayoutPanel historyToolbar;
        private readonly Panel appsTab;
        private readonly Panel processTab;
        private readonly Panel networkTab;
        private readonly Panel memoryTab;
        private Control activePage;
        private readonly Timer timer;
        private readonly System.Threading.SemaphoreSlim snapshotCollectionGate = new System.Threading.SemaphoreSlim(1, 1);
        private readonly Dictionary<DataGridView, Tuple<string, bool>> gridSortState = new Dictionary<DataGridView, Tuple<string, bool>>();

        private readonly Dictionary<int, Tuple<TimeSpan, DateTime, long>> lastCpu = new Dictionary<int, Tuple<TimeSpan, DateTime, long>>();
        private readonly object cpuCacheSync = new object();
        private List<ProcessRow> latestProcessRows = new List<ProcessRow>();
        private List<NetworkRow> latestNetworkRows = new List<NetworkRow>();
        private List<AppProfile> latestAppProfiles = new List<AppProfile>();
        private List<string[]> latestHistoryRows = new List<string[]>();
        private List<string[]> visibleHistoryRows = new List<string[]>();
        private HashSet<int> processPidScope;
        private DateTime latestAppsSnapshot = DateTime.MinValue;
        private DateTime latestProcessSnapshot = DateTime.MinValue;
        private DateTime latestNetworkSnapshot = DateTime.MinValue;
        private Dictionary<int, ProcessDetails> detailsCache = new Dictionary<int, ProcessDetails>();
        private readonly object detailsCacheSync = new object();
        private bool detailsLoaded = false;
        private bool refreshingApps = false;
        private bool refreshingProcesses = false;
        private bool refreshingNetwork = false;
        private bool updatingAppGrid = false;
        private bool settingProcessFilter = false;
        private bool loadingHistory = false;
        private bool refreshingHistory = false;
        private int historySortColumn = -1;
        private bool historySortAscending = true;
        private int historyPageStart = 0;
        private readonly Dictionary<string, string> firewallStatusCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly NetworkHistoryStore historyStore;
        private readonly AppSettingsStore settingsStore;
        private readonly NativeCpuCollector systemCpuCollector = new NativeCpuCollector();
        private readonly NetworkBandwidthSampler bandwidthSampler = new NetworkBandwidthSampler();
        private readonly ToolTip shortcutToolTip;
        private FormWindowState lastNonMinimizedWindowState = FormWindowState.Normal;

        public MainForm(bool skipInitialRefresh = false, string historyPath = null, string settingsPath = null)
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
            if (string.IsNullOrWhiteSpace(settingsPath)) settingsPath = Path.Combine(appDataFolder, "settings.json");
            settingsStore = new AppSettingsStore(settingsPath);
            shortcutToolTip = new ToolTip();
            AppSettings appSettings = settingsStore.Load();
            lastNonMinimizedWindowState = appSettings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Rectangle workingArea = Screen.PrimaryScreen == null ? SystemInformation.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int minimumWidth = Math.Min(1000, Math.Max(1, workingArea.Width));
            int minimumHeight = Math.Min(650, Math.Max(1, workingArea.Height));

            Text = "Better Task Manager v" + Application.ProductVersion;
            KeyPreview = true;
            MinimumSize = new Size(minimumWidth, minimumHeight);
            Size = new Size(
                ClampWindowDimension(appSettings.WindowWidth, minimumWidth, workingArea.Width, 1560),
                ClampWindowDimension(appSettings.WindowHeight, minimumHeight, workingArea.Height, 900));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;

            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (string.IsNullOrWhiteSpace(historyPath))
            {
                historyPath = Path.Combine(appDataFolder, "network-history.csv");
            }
            historyStore = new NetworkHistoryStore(historyPath);

            var rootShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            rootShell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(rootShell);

            navBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0), Cursor = Cursors.Hand };
            rootShell.Controls.Add(navBar, 0, 0);

            pageHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };
            rootShell.Controls.Add(pageHost, 0, 1);

            appsTab = MakePage("Apps");
            processTab = MakePage("Processes");
            networkTab = MakePage("Network");
            historyTab = MakePage("History");
            memoryTab = MakePage("Memory");
            pageHost.Controls.AddRange(new Control[] { appsTab, processTab, networkTab, historyTab, memoryTab });

            var appsNavButton = MakeNavButton("Apps");
            var processesNavButton = MakeNavButton("Processes");
            var networkNavButton = MakeNavButton("Network");
            var historyNavButton = MakeNavButton("History");
            var memoryNavButton = MakeNavButton("Memory");
            navBar.Controls.AddRange(new Control[] { appsNavButton, processesNavButton, networkNavButton, historyNavButton, memoryNavButton });

            liveMonitoringCheck = new CheckBox
            {
                Text = "Live monitoring",
                AutoSize = true,
                Checked = false,
                Margin = new Padding(18, 10, 6, 0)
            };
            refreshIntervalBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 78,
                Margin = new Padding(4, 5, 6, 0)
            };
            refreshIntervalBox.Items.AddRange(new object[] { "1 sec", "2 sec", "5 sec", "15 sec" });
            refreshIntervalBox.SelectedIndex = Math.Max(0, Math.Min(appSettings.RefreshIntervalIndex, refreshIntervalBox.Items.Count - 1));
            liveStatusLabel = new Label
            {
                Text = "Paused",
                AutoSize = true,
                ForeColor = Theme.MutedText,
                Margin = new Padding(4, 11, 0, 0)
            };
            restartAdminButton = MakeButton("Restart as Admin", 125);
            restartAdminButton.Height = 32;
            restartAdminButton.Margin = new Padding(14, 2, 6, 0);
            adminStatusLabel = new Label
            {
                Text = isAdmin ? "Administrator" : "Standard mode",
                AutoSize = true,
                Margin = new Padding(4, 11, 0, 0)
            };
            navBar.Controls.AddRange(new Control[] { liveMonitoringCheck, refreshIntervalBox, liveStatusLabel, restartAdminButton, adminStatusLabel });
            appsNavButton.Click += async (s, e) => await NavigateToPageAsync(appsTab);
            processesNavButton.Click += async (s, e) => await NavigateToPageAsync(processTab);
            networkNavButton.Click += async (s, e) => await NavigateToPageAsync(networkTab);
            historyNavButton.Click += async (s, e) => await NavigateToPageAsync(historyTab);
            memoryNavButton.Click += async (s, e) => await NavigateToPageAsync(memoryTab);

            var appShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
            appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            appsTab.Controls.Add(appShell);

            var appLeft = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(14), Margin = new Padding(0) };
            appLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            appLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            appLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appShell.Controls.Add(appLeft, 0, 0);

            var appHeader = new Label
            {
                Text = "Apps",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            appSearchBox = new TextBox { Dock = DockStyle.Fill };
            appSearchBox.PlaceholderText = "Search apps";
            appGrid = NewGrid();
            appGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            AddColumns(appGrid, new[] {
                Tuple.Create("App", "Application"),
                Tuple.Create("Firewall", "Firewall"),
                Tuple.Create("Processes", "Procs"),
                Tuple.Create("Connections", "Conn"),
                Tuple.Create("Cpu", "CPU %"),
                Tuple.Create("Ram", "WS MB"),
                Tuple.Create("Path", "Path")
            });
            appGrid.Columns["App"].Width = 145;
            appGrid.Columns["Firewall"].Width = 105;
            appGrid.Columns["Processes"].Width = 45;
            appGrid.Columns["Connections"].Width = 45;
            appGrid.Columns["Cpu"].Width = 65;
            appGrid.Columns["Cpu"].ToolTipText = "Sum of normalized per-PID CPU from the same Apps snapshot.";
            appGrid.Columns["Ram"].Width = 90;
            appGrid.Columns["Ram"].ToolTipText = "Sum of per-PID working sets; shared pages can overlap.";
            appGrid.Columns["Path"].Visible = false;
            LockGridColumns(appGrid);
            appLeft.Controls.Add(appHeader, 0, 0);
            appLeft.Controls.Add(appSearchBox, 0, 1);
            appLeft.Controls.Add(appGrid, 0, 2);

            var appRight = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(24, 18, 24, 18), Margin = new Padding(0) };
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            appRight.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            appRight.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            appRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appShell.Controls.Add(appRight, 1, 0);

            var selectedHeader = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            selectedHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            selectedHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appTitleLabel = new Label { Text = "Select an app", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 24, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            appMetaLabel = new Label { Text = "Refresh to load application activity", Dock = DockStyle.Fill, AutoEllipsis = true };
            selectedHeader.Controls.Add(appTitleLabel, 0, 0);
            selectedHeader.Controls.Add(appMetaLabel, 0, 1);
            appRight.Controls.Add(selectedHeader, 0, 0);

            appMetricCards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0) };
            appConnectionCard = MakeMetricCard("0", "Group Connections");
            appMemoryCard = MakeMetricCard("0 MB", "Sum Private Bytes");
            appRamCard = MakeMetricCard("0 MB", "Sum Working Set");
            appFirewallCard = MakeMetricCard("Unknown", "Firewall");
            appMetricCards.Controls.AddRange(new Control[] { appConnectionCard, appMemoryCard, appRamCard, appFirewallCard });
            appRight.Controls.Add(appMetricCards, 0, 1);

            appActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0) };
            appRefreshButton = MakeButton("Refresh Apps", 120);
            var exportAppsButton = MakeButton("Export CSV", 100);
            appBlockButton = MakeButton("Block App", 105);
            appUnblockButton = MakeButton("Unblock App", 115);
            appViewProcessesButton = MakeButton("View Processes", 125);
            appOpenFolderButton = MakeButton("Open Folder", 105);
            appCopyPathButton = MakeButton("Copy Path", 90);
            appFirewallDetailsLabel = new Label
            {
                Text = "Select an app to inspect its Better Task Manager firewall rule.",
                Width = 360,
                Height = 30,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.MutedText,
                Margin = new Padding(10, 0, 0, 0)
            };
            appActions.Controls.AddRange(new Control[] { appRefreshButton, exportAppsButton, appBlockButton, appUnblockButton, appViewProcessesButton, appOpenFolderButton, appCopyPathButton, appFirewallDetailsLabel });
            appRight.Controls.Add(appActions, 0, 2);

            appRight.Controls.Add(new Label { Text = "Connections", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft }, 0, 3);
            appConnectionsGrid = NewGrid();
            appConnectionsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            AddColumns(appConnectionsGrid, new[] {
                Tuple.Create("Protocol", "Protocol"),
                Tuple.Create("Local", "Local"),
                Tuple.Create("Remote", "Remote"),
                Tuple.Create("State", "State"),
                Tuple.Create("User", "User"),
                Tuple.Create("Path", "Application Path")
            });
            appConnectionsGrid.Columns["Protocol"].FillWeight = 45;
            appConnectionsGrid.Columns["Local"].FillWeight = 140;
            appConnectionsGrid.Columns["Remote"].FillWeight = 150;
            appConnectionsGrid.Columns["State"].FillWeight = 70;
            appConnectionsGrid.Columns["User"].FillWeight = 120;
            appConnectionsGrid.Columns["Path"].FillWeight = 300;
            LockGridColumns(appConnectionsGrid);
            appRight.Controls.Add(appConnectionsGrid, 0, 4);

            var processPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            processPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processTab.Controls.Add(processPanel);

            processToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 6, 8, 4) };
            refreshButton = MakeButton("Refresh", 90);
            killButton = MakeButton("Force Kill", 100);
            trimSelectedButton = MakeButton("Trim Selected Memory", 160);
            loadDetailsButton = MakeButton("Load Users/Paths", 130);
            processOpenFolderButton = MakeButton("Open Folder", 105);
            processCopyPathButton = MakeButton("Copy Path", 90);
            var exportProcessesButton = MakeButton("Export CSV", 100);
            var filterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(12, 9, 4, 0) };
            filterBox = new TextBox { Width = 260 };
            statusLabel = new Label
            {
                Text = isAdmin ? "Running as administrator" : "Not administrator: some actions may fail",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0),
                ForeColor = isAdmin ? Theme.Good : Theme.Danger
            };
            processToolbar.Controls.AddRange(new Control[] { refreshButton, killButton, trimSelectedButton, loadDetailsButton, exportProcessesButton, processOpenFolderButton, processCopyPathButton, filterLabel, filterBox, statusLabel });
            processPanel.Controls.Add(processToolbar, 0, 0);

            processSummaryLabel = new Label
            {
                Text = "Visible rows: 0",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 5, 8, 0),
                ForeColor = Theme.Info
            };
            processPanel.Controls.Add(processSummaryLabel, 0, 1);

            processGrid = NewGrid();
            AddColumns(processGrid, new[] {
                Tuple.Create("PID", "PID"),
                Tuple.Create("App", "Process"),
                Tuple.Create("User", "User"),
                Tuple.Create("CPU", "CPU %"),
                Tuple.Create("PrivateMB", "Private Bytes MB"),
                Tuple.Create("WorkingSetMB", "Working Set MB"),
                Tuple.Create("PeakWorkingSetMB", "Peak Working Set MB"),
                Tuple.Create("Threads", "Threads"),
                Tuple.Create("Path", "Application Path")
            });
            processGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            processGrid.Columns["PID"].Width = 70;
            processGrid.Columns["App"].Width = 310;
            processGrid.Columns["User"].Width = 190;
            processGrid.Columns["CPU"].Width = 70;
            processGrid.Columns["CPU"].ToolTipText = "Normalized CPU becomes available after a second snapshot for the same process instance.";
            processGrid.Columns["PrivateMB"].Width = 130;
            processGrid.Columns["WorkingSetMB"].Width = 150;
            processGrid.Columns["PeakWorkingSetMB"].Width = 120;
            processGrid.Columns["Threads"].Width = 80;
            processGrid.Columns["Path"].Width = 520;
            LockGridColumns(processGrid);
            processPanel.Controls.Add(processGrid, 0, 2);

            var networkPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            networkTab.Controls.Add(networkPanel);

            networkToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 6, 8, 4) };
            networkRefreshButton = MakeButton("Refresh", 90);
            blockButton = MakeButton("Block App", 100);
            unblockButton = MakeButton("Unblock App", 110);
            var exportNetworkButton = MakeButton("Export CSV", 100);
            networkOpenFolderButton = MakeButton("Open Folder", 105);
            networkCopyPathButton = MakeButton("Copy Path", 90);
            var networkFilterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(8, 9, 4, 0) };
            networkFilterBox = new TextBox { Width = 260, PlaceholderText = "App, PID, endpoint, state, path..." };
            networkStatusLabel = new Label
            {
                Text = "Live ports and destinations.",
                AutoSize = true,
                Margin = new Padding(8, 6, 4, 0)
            };
            bandwidthLabel = new Label
            {
                Text = "Total bandwidth: waiting for second sample",
                AutoSize = true,
                Margin = new Padding(16, 6, 4, 0),
                ForeColor = Theme.Info
            };
            networkToolbar.Controls.AddRange(new Control[] { networkRefreshButton, blockButton, unblockButton, exportNetworkButton, networkOpenFolderButton, networkCopyPathButton, networkFilterLabel, networkFilterBox });
            networkPanel.Controls.Add(networkToolbar, 0, 0);

            var networkInfoBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 0, 8, 0) };
            networkInfoBar.Controls.AddRange(new Control[] { networkStatusLabel, bandwidthLabel });
            networkPanel.Controls.Add(networkInfoBar, 0, 1);

            networkGrid = NewGrid();
            AddColumns(networkGrid, new[] {
                Tuple.Create("Process", "Application"),
                Tuple.Create("PID", "PID"),
                Tuple.Create("User", "User"),
                Tuple.Create("Protocol", "Protocol"),
                Tuple.Create("LocalAddress", "Local Address"),
                Tuple.Create("LocalPort", "Local Port"),
                Tuple.Create("RemoteAddress", "Remote Address"),
                Tuple.Create("RemotePort", "Remote Port"),
                Tuple.Create("State", "State"),
                Tuple.Create("Path", "Application Path")
            });
            networkGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            networkGrid.Columns["Process"].Width = 180;
            networkGrid.Columns["PID"].Width = 70;
            networkGrid.Columns["User"].Width = 180;
            networkGrid.Columns["Protocol"].Width = 80;
            networkGrid.Columns["LocalAddress"].Width = 180;
            networkGrid.Columns["LocalPort"].Width = 85;
            networkGrid.Columns["RemoteAddress"].Width = 180;
            networkGrid.Columns["RemotePort"].Width = 85;
            networkGrid.Columns["State"].Width = 110;
            networkGrid.Columns["Path"].Width = 500;
            LockGridColumns(networkGrid);
            networkPanel.Controls.Add(networkGrid, 0, 2);

            var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            historyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            historyTab.Controls.Add(historyPanel);
            historyToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 6, 8, 4) };
            reloadHistoryButton = MakeButton("Reload History", 120);
            var exportHistoryButton = MakeButton("Export CSV", 100);
            clearHistoryButton = MakeButton("Clear History", 105);
            historyPreviousButton = MakeButton("Previous", 80);
            historyNextButton = MakeButton("Next", 65);
            historyPreviousButton.Enabled = false;
            historyNextButton.Enabled = false;
            var historyFilterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(8, 9, 4, 0) };
            historyFilterBox = new TextBox { Width = 260, PlaceholderText = "App, PID, address, state, path..." };
            historyNoteLabel = new Label
            {
                Text = "Shows new and changed connections from the last 30 days (newest first).",
                AutoSize = true,
                Margin = new Padding(12, 9, 4, 0)
            };
            historyToolbar.Controls.AddRange(new Control[] { reloadHistoryButton, exportHistoryButton, clearHistoryButton, historyPreviousButton, historyNextButton, historyFilterLabel, historyFilterBox, historyNoteLabel });
            historyPanel.Controls.Add(historyToolbar, 0, 0);
            historyList = new BufferedListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                GridLines = true,
                VirtualMode = true,
                VirtualListSize = 0,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None
            };
            historyList.Columns.Add("Timestamp", 150);
            historyList.Columns.Add("Application", 140);
            historyList.Columns.Add("PID", 75);
            historyList.Columns.Add("User", 180);
            historyList.Columns.Add("Protocol", 80);
            historyList.Columns.Add("Local Address", 180);
            historyList.Columns.Add("Local Port", 85);
            historyList.Columns.Add("Remote Address", 180);
            historyList.Columns.Add("Remote Port", 85);
            historyList.Columns.Add("State", 100);
            historyList.Columns.Add("Application Path", 420);
            historyList.RetrieveVirtualItem += HistoryListRetrieveVirtualItem;
            historyList.ColumnClick += HistoryListColumnClick;
            historyList.HandleCreated += (s, e) => ApplyNativeDarkTheme(historyList);
            historyPanel.Controls.Add(historyList, 0, 1);

            var memoryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            memoryTab.Controls.Add(memoryPanel);
            memoryPanel.Controls.Add(new Label { Text = "Memory", Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true });
            memorySnapshotLabel = new Label { Text = "Snapshot unavailable", AutoSize = true, Width = 1400, ForeColor = Theme.MutedText };
            memoryPanel.Controls.Add(memorySnapshotLabel);

            var memoryCards = new FlowLayoutPanel { Width = 1400, Height = 84, WrapContents = false, Margin = new Padding(0, 8, 0, 8) };
            memoryCpuCard = MakeMetricCard("...", "System CPU");
            memoryLoadCard = MakeMetricCard("0%", "Physical Load");
            memoryUsedCard = MakeMetricCard("0 GiB", "Used RAM");
            memoryAvailableCard = MakeMetricCard("0 GiB", "Available RAM");
            memoryCommitCard = MakeMetricCard("0 / 0 GiB", "Commit / Limit");
            memoryCacheCard = MakeMetricCard("0 GiB", "System Cache");
            memoryCards.Controls.AddRange(new Control[] { memoryCpuCard, memoryLoadCard, memoryUsedCard, memoryAvailableCard, memoryCommitCard, memoryCacheCard });
            memoryPanel.Controls.Add(memoryCards);

            memoryPanel.Controls.Add(new Label { Text = "Maintenance actions", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
            var memoryActions = new FlowLayoutPanel { Width = 1400, Height = 40, WrapContents = false, Margin = new Padding(0) };
            memoryRefreshButton = MakeButton("Refresh", 90);
            trimAllButton = MakeButton("Trim App Memory", 260);
            clearStandbyButton = MakeButton("Clear Standby Cache", 260);
            emptySystemButton = MakeButton("Release System Cache", 260);
            memoryActions.Controls.AddRange(new Control[] { memoryRefreshButton, trimAllButton, clearStandbyButton, emptySystemButton });
            memoryPanel.Controls.Add(memoryActions);
            memoryPanel.Controls.Add(new Label
            {
                Text = "Windows uses available RAM for cache intentionally. Cleanup actions are troubleshooting tools, not routine optimization.",
                AutoSize = true,
                Width = 1400,
                ForeColor = Theme.MutedText,
                Margin = new Padding(0, 8, 0, 2)
            });
            memoryStatusLabel = new Label { Text = "", AutoSize = true, Width = 1400 };
            memoryPanel.Controls.Add(memoryStatusLabel);

            shortcutToolTip.SetToolTip(liveMonitoringCheck, "Toggle Live monitoring (Ctrl+L)");
            shortcutToolTip.SetToolTip(appsNavButton, "Open Apps (Ctrl+1)");
            shortcutToolTip.SetToolTip(processesNavButton, "Open Processes (Ctrl+2)");
            shortcutToolTip.SetToolTip(networkNavButton, "Open Network (Ctrl+3)");
            shortcutToolTip.SetToolTip(historyNavButton, "Open History (Ctrl+4)");
            shortcutToolTip.SetToolTip(memoryNavButton, "Open Memory (Ctrl+5)");
            shortcutToolTip.SetToolTip(historyPreviousButton, "Previous History page (Page Up)");
            shortcutToolTip.SetToolTip(historyNextButton, "Next History page (Page Down)");
            shortcutToolTip.SetToolTip(appSearchBox, "Focus search (Ctrl+F); clear search (Escape)");
            shortcutToolTip.SetToolTip(filterBox, "Focus search (Ctrl+F); clear search (Escape)");
            shortcutToolTip.SetToolTip(networkFilterBox, "Focus search (Ctrl+F); clear search (Escape)");
            shortcutToolTip.SetToolTip(historyFilterBox, "Focus search (Ctrl+F); clear search (Escape)");
            foreach (Button button in new[] { appRefreshButton, refreshButton, networkRefreshButton, reloadHistoryButton, memoryRefreshButton })
            {
                shortcutToolTip.SetToolTip(button, "Refresh active view (F5)");
            }
            foreach (Button button in new[] { exportAppsButton, exportProcessesButton, exportNetworkButton, exportHistoryButton })
            {
                shortcutToolTip.SetToolTip(button, "Export active view (Ctrl+E)");
            }
            foreach (Button button in new[] { appOpenFolderButton, processOpenFolderButton, networkOpenFolderButton })
            {
                shortcutToolTip.SetToolTip(button, "Open the selected executable's folder");
            }
            foreach (Button button in new[] { appCopyPathButton, processCopyPathButton, networkCopyPathButton })
            {
                shortcutToolTip.SetToolTip(button, "Copy the selected executable path");
            }

            appRefreshButton.Click += async (s, e) => await RefreshAppsAsync(true);
            exportAppsButton.Click += async (s, e) => await ExportAppsAsync();
            appSearchBox.TextChanged += (s, e) => { FillAppGridFromCache(); ShowSelectedApp(); };
            appGrid.SelectionChanged += (s, e) => ShowSelectedApp();
            appBlockButton.Click += async (s, e) => await BlockSelectedAppAsync(true);
            appUnblockButton.Click += async (s, e) => await BlockSelectedAppAsync(false);
            appViewProcessesButton.Click += (s, e) => ViewSelectedAppProcesses();
            appOpenFolderButton.Click += (s, e) => OpenSelectedExecutableFolder();
            appCopyPathButton.Click += async (s, e) => await CopySelectedExecutablePathAsync();

            refreshButton.Click += async (s, e) => await RefreshProcessesAsync();
            loadDetailsButton.Click += async (s, e) => await LoadDetailsAndRefreshAsync();
            filterBox.TextChanged += (s, e) =>
            {
                if (settingProcessFilter) return;
                processPidScope = null;
                FillProcessGridFromCache();
            };
            killButton.Click += async (s, e) => await KillSelectedAsync();
            trimSelectedButton.Click += async (s, e) => await TrimSelectedAsync();
            exportProcessesButton.Click += async (s, e) => await ExportProcessesAsync();
            restartAdminButton.Click += (s, e) => RestartAsAdmin();
            processGrid.SelectionChanged += (s, e) => UpdateExecutablePathActions();
            processOpenFolderButton.Click += (s, e) => OpenSelectedExecutableFolder();
            processCopyPathButton.Click += async (s, e) => await CopySelectedExecutablePathAsync();

            networkRefreshButton.Click += async (s, e) => await RefreshNetworkAsync();
            networkFilterBox.TextChanged += (s, e) => FillNetworkGridFromCache();
            blockButton.Click += async (s, e) => await BlockSelectedAsync(true);
            unblockButton.Click += async (s, e) => await BlockSelectedAsync(false);
            exportNetworkButton.Click += async (s, e) => await ExportNetworkAsync();
            networkGrid.SelectionChanged += (s, e) => UpdateExecutablePathActions();
            networkOpenFolderButton.Click += (s, e) => OpenSelectedExecutableFolder();
            networkCopyPathButton.Click += async (s, e) => await CopySelectedExecutablePathAsync();
            reloadHistoryButton.Click += async (s, e) => await LoadHistoryGridAsync();
            exportHistoryButton.Click += async (s, e) => await ExportHistoryAsync();
            clearHistoryButton.Click += async (s, e) => await ClearHistoryAsync();
            historyPreviousButton.Click += (s, e) => MoveHistoryPage(-1);
            historyNextButton.Click += (s, e) => MoveHistoryPage(1);
            historyFilterBox.TextChanged += (s, e) => FillHistoryGrid(true);

            trimAllButton.Click += async (s, e) => await TrimAllAsync();
            clearStandbyButton.Click += (s, e) => ClearStandby();
            emptySystemButton.Click += (s, e) => EmptySystemWorkingSets();
            memoryRefreshButton.Click += (s, e) => RefreshMemoryPage();

            timer = new Timer { Interval = 5000, Enabled = false };
            timer.Tick += async (s, e) =>
            {
                if (!liveMonitoringCheck.Checked) return;
                await RefreshActivePageAsync(true);
            };
            liveMonitoringCheck.CheckedChanged += async (s, e) =>
            {
                timer.Enabled = liveMonitoringCheck.Checked;
                liveStatusLabel.Text = liveMonitoringCheck.Checked ? "Live" : "Paused";
                liveStatusLabel.ForeColor = liveMonitoringCheck.Checked ? Theme.Good : Theme.MutedText;
                if (liveMonitoringCheck.Checked) await RefreshActivePageAsync(true);
            };
            refreshIntervalBox.SelectedIndexChanged += (s, e) =>
            {
                timer.Interval = RefreshIntervalMilliseconds(refreshIntervalBox.SelectedIndex);
            };
            KeyDown += async (s, e) =>
            {
                if (await HandleGlobalShortcutAsync(e.KeyData))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            Shown += async (s, e) =>
            {
                ApplyDarkTheme(this);
                ApplyNativeDarkTheme(this);
                ApplyPrivilegeState();
                if (appSettings.Maximized) WindowState = FormWindowState.Maximized;
                ShowPage(appsTab);
                if (!skipInitialRefresh) await RefreshAppsAsync(true);
            };
            FormClosing += (s, e) => SaveAppSettings();
            FormClosed += (s, e) => shortcutToolTip.Dispose();
            Resize += (s, e) =>
            {
                if (WindowState != FormWindowState.Minimized) lastNonMinimizedWindowState = WindowState;
            };

            RestoreColumnWidths(appSettings.ColumnWidths);
            ApplyDarkTheme(this);
            ApplyPrivilegeState();
            ShowPage(appsTab);
            ShowSelectedApp();
            UpdateExecutablePathActions();
        }

        private void ApplyPrivilegeState()
        {
            restartAdminButton.Visible = !isAdmin;
            adminStatusLabel.Text = isAdmin ? "Administrator" : "Standard mode";
            adminStatusLabel.ForeColor = isAdmin ? Theme.Good : Theme.MutedText;
            blockButton.Enabled = isAdmin;
            unblockButton.Enabled = isAdmin;
            clearStandbyButton.Enabled = isAdmin;
            emptySystemButton.Enabled = isAdmin;
        }

        internal static GlobalShortcutCommand GetGlobalShortcutCommand(Keys keyData, string pageName)
        {
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1)) return GlobalShortcutCommand.OpenApps;
            if (keyData == (Keys.Control | Keys.D2) || keyData == (Keys.Control | Keys.NumPad2)) return GlobalShortcutCommand.OpenProcesses;
            if (keyData == (Keys.Control | Keys.D3) || keyData == (Keys.Control | Keys.NumPad3)) return GlobalShortcutCommand.OpenNetwork;
            if (keyData == (Keys.Control | Keys.D4) || keyData == (Keys.Control | Keys.NumPad4)) return GlobalShortcutCommand.OpenHistory;
            if (keyData == (Keys.Control | Keys.D5) || keyData == (Keys.Control | Keys.NumPad5)) return GlobalShortcutCommand.OpenMemory;
            if (keyData == Keys.F5) return GlobalShortcutCommand.Refresh;
            if (keyData == (Keys.Control | Keys.L)) return GlobalShortcutCommand.ToggleLive;

            bool searchablePage = pageName == "Apps" || pageName == "Processes" || pageName == "Network" || pageName == "History";
            if (keyData == (Keys.Control | Keys.F) && searchablePage) return GlobalShortcutCommand.FocusFilter;
            if (keyData == Keys.Escape && searchablePage) return GlobalShortcutCommand.ClearFilter;
            if (keyData == (Keys.Control | Keys.E) && searchablePage) return GlobalShortcutCommand.Export;
            if (keyData == Keys.PageUp && pageName == "History") return GlobalShortcutCommand.PreviousPage;
            if (keyData == Keys.PageDown && pageName == "History") return GlobalShortcutCommand.NextPage;
            return GlobalShortcutCommand.None;
        }

        private async Task<bool> HandleGlobalShortcutAsync(Keys keyData)
        {
            string pageName = Convert.ToString(activePage == null ? null : activePage.Tag) ?? "";
            GlobalShortcutCommand command = GetGlobalShortcutCommand(keyData, pageName);
            if (command == GlobalShortcutCommand.None) return false;

            if (command == GlobalShortcutCommand.OpenApps) { await NavigateToPageAsync(appsTab); return true; }
            if (command == GlobalShortcutCommand.OpenProcesses) { await NavigateToPageAsync(processTab); return true; }
            if (command == GlobalShortcutCommand.OpenNetwork) { await NavigateToPageAsync(networkTab); return true; }
            if (command == GlobalShortcutCommand.OpenHistory) { await NavigateToPageAsync(historyTab); return true; }
            if (command == GlobalShortcutCommand.OpenMemory) { await NavigateToPageAsync(memoryTab); return true; }
            if (command == GlobalShortcutCommand.PreviousPage) { MoveHistoryPage(-1); return true; }
            if (command == GlobalShortcutCommand.NextPage) { MoveHistoryPage(1); return true; }

            if (command == GlobalShortcutCommand.ToggleLive)
            {
                liveMonitoringCheck.Checked = !liveMonitoringCheck.Checked;
                return true;
            }

            if (command == GlobalShortcutCommand.Refresh)
            {
                await RefreshCurrentPageManuallyAsync();
                return true;
            }

            TextBox filter = ActiveFilterBox();
            if (command == GlobalShortcutCommand.FocusFilter)
            {
                if (filter == null) return false;
                filter.Focus();
                filter.SelectAll();
                return true;
            }
            if (command == GlobalShortcutCommand.ClearFilter)
            {
                if (filter == null) return false;
                filter.Clear();
                return true;
            }

            await ExportCurrentPageAsync();
            return true;
        }

        private async Task NavigateToPageAsync(Control page)
        {
            if (page == historyTab)
            {
                await ShowHistoryAsync();
                return;
            }

            ShowPage(page);
            if (page == appsTab) await RefreshAppsAsync(false);
            else if (page == processTab) await RefreshProcessesAsync();
            else if (page == networkTab) await RefreshNetworkAsync();
            else if (page == memoryTab) RefreshMemoryPage();
        }

        private TextBox ActiveFilterBox()
        {
            if (activePage == appsTab) return appSearchBox;
            if (activePage == processTab) return filterBox;
            if (activePage == networkTab) return networkFilterBox;
            if (activePage == historyTab) return historyFilterBox;
            return null;
        }

        private async Task RefreshCurrentPageManuallyAsync()
        {
            if (activePage == appsTab) await RefreshAppsAsync(true);
            else if (activePage == processTab) await RefreshProcessesAsync();
            else if (activePage == networkTab) await RefreshNetworkAsync();
            else if (activePage == historyTab) await LoadHistoryGridAsync();
            else if (activePage == memoryTab) RefreshMemoryPage();
        }

        private async Task ExportCurrentPageAsync()
        {
            if (activePage == appsTab) await ExportAppsAsync();
            else if (activePage == processTab) await ExportProcessesAsync();
            else if (activePage == networkTab) await ExportNetworkAsync();
            else if (activePage == historyTab) await ExportHistoryAsync();
        }

        private void SaveAppSettings()
        {
            try
            {
                Size savedSize = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
                settingsStore.Save(new AppSettings
                {
                    WindowWidth = savedSize.Width,
                    WindowHeight = savedSize.Height,
                    Maximized = ShouldPersistMaximized(WindowState, lastNonMinimizedWindowState),
                    RefreshIntervalIndex = Math.Max(0, refreshIntervalBox.SelectedIndex),
                    ColumnWidths = CaptureColumnWidths()
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void RestoreColumnWidths(Dictionary<string, int> widths)
        {
            if (widths == null) return;
            RestoreGridColumnWidths(appGrid, "Apps", widths);
            RestoreGridColumnWidths(processGrid, "Processes", widths);
            RestoreGridColumnWidths(networkGrid, "Network", widths);
            for (int index = 0; index < historyList.Columns.Count; index++)
            {
                int width;
                if (widths.TryGetValue("History." + index.ToString(CultureInfo.InvariantCulture), out width))
                {
                    historyList.Columns[index].Width = ClampColumnWidth(width);
                }
            }
        }

        private static void RestoreGridColumnWidths(DataGridView grid, string prefix, Dictionary<string, int> widths)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                int width;
                if (widths.TryGetValue(prefix + "." + column.Name, out width)) column.Width = ClampColumnWidth(width);
            }
        }

        private Dictionary<string, int> CaptureColumnWidths()
        {
            var widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            CaptureGridColumnWidths(appGrid, "Apps", widths);
            CaptureGridColumnWidths(processGrid, "Processes", widths);
            CaptureGridColumnWidths(networkGrid, "Network", widths);
            for (int index = 0; index < historyList.Columns.Count; index++)
            {
                widths["History." + index.ToString(CultureInfo.InvariantCulture)] = historyList.Columns[index].Width;
            }
            return widths;
        }

        private static void CaptureGridColumnWidths(DataGridView grid, string prefix, Dictionary<string, int> widths)
        {
            foreach (DataGridViewColumn column in grid.Columns) widths[prefix + "." + column.Name] = column.Width;
        }

        internal static int ClampColumnWidth(int width)
        {
            return Math.Max(40, Math.Min(width, 1200));
        }

        internal static bool ShouldPersistMaximized(FormWindowState current, FormWindowState lastNonMinimized)
        {
            return current == FormWindowState.Maximized ||
                (current == FormWindowState.Minimized && lastNonMinimized == FormWindowState.Maximized);
        }

        private async Task RefreshActivePageAsync(bool automatic = false)
        {
            if (activePage == appsTab) await RefreshAppsAsync(false, automatic);
            else if (activePage == processTab) await RefreshProcessesAsync(automatic);
            else if (activePage == networkTab) await RefreshNetworkAsync(automatic);
            else if (activePage == historyTab) await RefreshHistoryLiveAsync();
            else if (activePage == memoryTab)
            {
                if (RefreshMemoryPage()) MarkLiveRefreshSuccess();
                else MarkLiveRefreshFailure();
            }
        }

        internal static bool ShouldShowRefreshDialog(bool automatic)
        {
            return !automatic;
        }

        private void MarkLiveRefreshSuccess()
        {
            if (!liveMonitoringCheck.Checked) return;
            liveStatusLabel.Text = "Live";
            liveStatusLabel.ForeColor = Theme.Good;
        }

        private void MarkLiveRefreshFailure()
        {
            if (!liveMonitoringCheck.Checked) return;
            liveStatusLabel.Text = "Live error";
            liveStatusLabel.ForeColor = Theme.Danger;
        }

        private async Task<T> RunSnapshotCollectionAsync<T>(Control expectedPage, Func<T> collector) where T : class
        {
            await snapshotCollectionGate.WaitAsync();
            try
            {
                if (IsDisposed || activePage != expectedPage) return null;
                T result = await Task.Run(collector);
                return IsDisposed || activePage != expectedPage ? null : result;
            }
            finally
            {
                snapshotCollectionGate.Release();
            }
        }

        internal static int RefreshIntervalMilliseconds(int selectedIndex)
        {
            switch (selectedIndex)
            {
                case 0: return 1000;
                case 1: return 2000;
                case 3: return 15000;
                default: return 5000;
            }
        }

        internal static int ClampWindowDimension(int value, int minimum, int maximum, int fallback)
        {
            int safeMinimum = Math.Max(1, minimum);
            int safeMaximum = Math.Max(safeMinimum, maximum);
            int candidate = value > 0 ? value : fallback;
            return Math.Max(safeMinimum, Math.Min(candidate, safeMaximum));
        }

        internal static string SnapshotLabel(DateTime snapshot)
        {
            return snapshot == DateTime.MinValue
                ? "Snapshot unavailable"
                : "Snapshot " + snapshot.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnableDarkTitleBar();
            ApplyNativeDarkTheme(this);
        }

        private void EnableDarkTitleBar()
        {
            try
            {
                int enabled = 1;
                int result = NativeMethods.DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                if (result != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                }
            }
            catch { }
        }

        private static Button MakeButton(string text, int width)
        {
            return new Button { Text = text, Width = width, Height = 30, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
        }

        private static Button MakeNavButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 138,
                Height = 36,
                Margin = new Padding(0, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand
            };
        }

        private static Panel MakePage(string name)
        {
            return new Panel
            {
                Tag = name,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false,
                BackColor = Theme.Window,
                ForeColor = Theme.Text
            };
        }

        private void ShowPage(Control page)
        {
            if (page == null || pageHost == null) return;
            foreach (Control control in pageHost.Controls)
            {
                control.Visible = false;
            }
            activePage = page;
            page.Visible = true;
            page.BringToFront();
            UpdateNavButtons(navBar);
        }

        private void UpdateNavButtons(FlowLayoutPanel navBar)
        {
            if (navBar == null || activePage == null) return;

            foreach (Control control in navBar.Controls)
            {
                var button = control as Button;
                if (button == null) continue;

                string activeName = Convert.ToString(activePage.Tag) ?? "";
                bool selected = string.Equals(button.Text, activeName, StringComparison.OrdinalIgnoreCase);

                button.BackColor = selected ? Theme.AccentSelected : Theme.SurfaceAlt;
                button.ForeColor = selected ? Color.White : Theme.Text;
                button.FlatAppearance.BorderColor = selected ? Theme.AccentHover : Theme.Border;
                button.FlatAppearance.MouseOverBackColor = selected ? Theme.AccentHover : Theme.SurfaceRaised;
                button.FlatAppearance.MouseDownBackColor = Theme.Accent;
            }
        }

        private static Label MakeMetricCard(string value, string caption)
        {
            return new Label
            {
                Text = value + "\n" + caption,
                Width = 176,
                Height = 64,
                Margin = new Padding(0, 8, 14, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static DataGridView NewGrid()
        {
            var grid = new BufferedDataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                RowHeadersVisible = false,
                BackgroundColor = Theme.Window,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            StyleGrid(grid);
            grid.HandleCreated += (s, e) => ApplyNativeDarkTheme(grid);
            return grid;
        }

        private static void AddColumns(DataGridView grid, IEnumerable<Tuple<string, string>> columns)
        {
            foreach (var column in columns) grid.Columns.Add(column.Item1, column.Item2);
        }

        private void LockGridColumns(DataGridView grid)
        {
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 28;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
                column.Resizable = DataGridViewTriState.True;
            }
            grid.ColumnHeaderMouseClick -= GridColumnHeaderMouseClick;
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
        }

        private void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null || e.ColumnIndex < 0) return;

            string columnName = grid.Columns[e.ColumnIndex].Name;
            Tuple<string, bool> old;
            bool ascending = true;
            if (gridSortState.TryGetValue(grid, out old) && old.Item1 == columnName)
            {
                ascending = !old.Item2;
            }
            gridSortState[grid] = Tuple.Create(columnName, ascending);

            if (grid == appGrid)
            {
                FillAppGridFromCache();
                ShowSelectedApp();
            }
            else if (grid == appConnectionsGrid)
            {
                SortVisibleGrid(grid, columnName, ascending);
            }
            else if (grid == processGrid)
            {
                FillProcessGridFromCache();
            }
            else if (grid == networkGrid)
            {
                FillNetworkGridFromCache();
            }
            ApplySortGlyph(grid, columnName, ascending);
        }

        private static void ApplySortGlyph(DataGridView grid, string columnName, bool ascending)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            grid.Columns[columnName].HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
        }

        private List<AppProfile> SortApps(List<AppProfile> apps, string columnName, bool ascending)
        {
            IEnumerable<AppProfile> query;
            if (columnName == "Processes") query = apps.OrderBy(a => a.Pids.Count);
            else if (columnName == "Connections") query = apps.OrderBy(a => a.ConnectionCount);
            else if (columnName == "Cpu") query = apps.OrderBy(a => a.CpuSampleCount == 0 ? double.MinValue : a.Cpu);
            else if (columnName == "Ram") query = apps.OrderBy(a => a.RamMb);
            else if (columnName == "Firewall") query = apps.OrderBy(a => GetFirewallStatus(a.Path), StringComparer.OrdinalIgnoreCase);
            else query = apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
            if (!ascending) query = query.Reverse();
            return query.ToList();
        }

        private static List<ProcessRow> SortProcesses(List<ProcessRow> rows, string columnName, bool ascending)
        {
            IEnumerable<ProcessRow> query;
            if (columnName == "PID") query = rows.OrderBy(r => r.Pid);
            else if (columnName == "CPU") query = rows.OrderBy(r => r.CpuSampleAvailable ? r.Cpu : double.MinValue);
            else if (columnName == "PrivateMB") query = rows.OrderBy(r => r.PrivateMb);
            else if (columnName == "WorkingSetMB") query = rows.OrderBy(r => r.WorkingSetMb);
            else if (columnName == "PeakWorkingSetMB") query = rows.OrderBy(r => r.PeakWorkingSetMb);
            else if (columnName == "Threads") query = rows.OrderBy(r => r.Threads);
            else if (columnName == "User") query = rows.OrderBy(r => r.User, StringComparer.OrdinalIgnoreCase);
            else if (columnName == "Path") query = rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase);
            else query = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
            if (!ascending) query = query.Reverse();
            return query.ToList();
        }

        private static List<NetworkRow> SortNetworkRows(List<NetworkRow> rows, string columnName, bool ascending)
        {
            IEnumerable<NetworkRow> query;
            if (columnName == "PID") query = rows.OrderBy(row => row.Pid);
            else if (columnName == "LocalPort") query = rows.OrderBy(row => PortSortValue(row.LocalPort));
            else if (columnName == "RemotePort") query = rows.OrderBy(row => PortSortValue(row.RemotePort));
            else if (columnName == "User") query = rows.OrderBy(row => row.User, StringComparer.OrdinalIgnoreCase);
            else if (columnName == "Protocol") query = rows.OrderBy(row => row.Protocol, StringComparer.OrdinalIgnoreCase);
            else if (columnName == "LocalAddress") query = rows.OrderBy(row => row.LocalAddress, StringComparer.OrdinalIgnoreCase);
            else if (columnName == "RemoteAddress") query = rows.OrderBy(row => row.RemoteAddress, StringComparer.OrdinalIgnoreCase);
            else if (columnName == "State") query = rows.OrderBy(row => NormalizeConnectionState(row.State), StringComparer.OrdinalIgnoreCase);
            else if (columnName == "Path") query = rows.OrderBy(row => row.Path, StringComparer.OrdinalIgnoreCase);
            else query = rows.OrderBy(row => row.Process, StringComparer.OrdinalIgnoreCase);
            if (!ascending) query = query.Reverse();
            return query.ToList();
        }

        private static int PortSortValue(string value)
        {
            int port;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ? port : -1;
        }

        private static void SortVisibleGrid(DataGridView grid, string columnName, bool ascending)
        {
            var rows = grid.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray())
                .ToList();

            int index = grid.Columns[columnName].Index;
            rows = rows.OrderBy(r => SortKey(r[index])).ToList();
            if (!ascending) rows.Reverse();

            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear();
                foreach (var row in rows) grid.Rows.Add(row);
            }
            finally
            {
                grid.ResumeLayout();
            }
        }

        private static IComparable SortKey(object value)
        {
            if (value == null) return "";
            double number;
            string text = Convert.ToString(value);
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out number)) return number;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
            return text ?? "";
        }

        private void DrawDarkTab(object sender, DrawItemEventArgs e)
        {
            var tab = (TabControl)sender;
            bool selected = e.Index == tab.SelectedIndex;
            Rectangle bounds = e.Bounds;
            Color background = selected ? Theme.SurfaceAlt : Theme.Window;
            Color foreground = selected ? Color.White : Theme.MutedText;

            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                tab.TabPages[e.Index].Text,
                Font,
                bounds,
                foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (selected)
            {
                using (var pen = new Pen(Theme.AccentHover, 2))
                {
                    e.Graphics.DrawLine(pen, bounds.Left + 8, bounds.Bottom - 2, bounds.Right - 8, bounds.Bottom - 2);
                }
            }
        }

        private async Task RefreshAppsAsync(bool refreshFirewall, bool automatic = false)
        {
            if (refreshingApps) return;
            refreshingApps = true;
            appRefreshButton.Enabled = false;
            appTitleLabel.Text = "Loading apps...";
            appMetaLabel.Text = "Collecting processes, connections, and firewall state";
            try
            {
                Dictionary<int, ProcessDetails> cache;
                lock (detailsCacheSync) cache = detailsCache;
                var knownFirewallStatuses = new Dictionary<string, string>(firewallStatusCache, StringComparer.OrdinalIgnoreCase);
                var data = await RunSnapshotCollectionAsync(appsTab, () =>
                {
                    DateTime snapshotTime = DateTime.Now;
                    var processes = BuildProcessRows(cache);
                    var network = BuildNetworkRows(processes);
                    var apps = BuildAppProfiles(processes, network);
                    var firewall = refreshFirewall ? LoadFirewallStatuses(apps) : knownFirewallStatuses;
                    SaveNetworkHistory(network);
                    return Tuple.Create(processes, network, apps, firewall, snapshotTime);
                });
                if (data == null) return;

                latestProcessRows = data.Item1;
                latestNetworkRows = data.Item2;
                latestAppProfiles = data.Item3;
                latestAppsSnapshot = data.Item5;
                latestProcessSnapshot = data.Item5;
                latestNetworkSnapshot = data.Item5;
                firewallStatusCache.Clear();
                foreach (var pair in data.Item4) firewallStatusCache[pair.Key] = pair.Value;
                FillAppGridFromCache();
                UpdateBandwidthLabel();
                ShowSelectedApp();
                MarkLiveRefreshSuccess();
            }
            catch (Exception ex)
            {
                appTitleLabel.Text = "Refresh failed";
                appMetaLabel.Text = ex.Message;
                MarkLiveRefreshFailure();
            }
            finally
            {
                appRefreshButton.Enabled = true;
                refreshingApps = false;
            }
        }

        private void HistoryListColumnClick(object sender, ColumnClickEventArgs e)
        {
            historySortAscending = e.Column == historySortColumn ? !historySortAscending : true;
            historySortColumn = e.Column;
            historyPageStart = 0;
            SortHistoryRows();
            UpdateHistoryPage();
        }

        private void SortHistoryRows()
        {
            if (historySortColumn < 0) return;
            visibleHistoryRows = SortHistoryRowsForView(visibleHistoryRows, historySortColumn, historySortAscending);
        }

        internal static List<string[]> SortHistoryRowsForView(IEnumerable<string[]> rows, int columnIndex, bool ascending)
        {
            var source = (rows ?? Enumerable.Empty<string[]>()).ToList();
            IEnumerable<string[]> sorted;
            if (columnIndex == 0)
            {
                sorted = source.OrderBy(row => HistoryTimestampSortValue(HistoryField(row, columnIndex)));
            }
            else if (columnIndex == 2 || columnIndex == 6 || columnIndex == 8)
            {
                sorted = source.OrderBy(row => PortSortValue(HistoryField(row, columnIndex)));
            }
            else
            {
                sorted = source.OrderBy(row => HistoryField(row, columnIndex), StringComparer.OrdinalIgnoreCase);
            }
            if (!ascending) sorted = sorted.Reverse();
            return sorted.ToList();
        }

        private static string HistoryField(string[] row, int columnIndex)
        {
            return row != null && columnIndex >= 0 && columnIndex < row.Length ? row[columnIndex] ?? "" : "";
        }

        private static long HistoryTimestampSortValue(string value)
        {
            DateTime timestamp;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp) ||
                DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out timestamp))
            {
                return timestamp.Ticks;
            }
            return long.MinValue;
        }

        private static Dictionary<string, string> LoadFirewallStatuses(IEnumerable<AppProfile> apps)
        {
            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var paths = apps.Select(a => a.Path).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) return statuses;

            CommandResult result = CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "show", "rule", "name=all");
            foreach (string path in paths)
            {
                statuses[path] = result.Succeeded && result.StandardOutput.IndexOf(RuleNameForPath(path), StringComparison.OrdinalIgnoreCase) >= 0
                    ? FirewallStatusBlocked
                    : result.Succeeded ? FirewallStatusNoBlock : "Unknown";
            }

            return statuses;
        }

        internal static List<AppProfile> BuildAppProfiles(List<ProcessRow> processes, List<NetworkRow> network)
        {
            var apps = new Dictionary<string, AppProfile>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in processes)
            {
                if (row.Pid == 0) continue;
                string key = !string.IsNullOrWhiteSpace(row.Path) ? row.Path : row.Name;
                if (string.IsNullOrWhiteSpace(key)) key = "PID " + row.Pid.ToString(CultureInfo.InvariantCulture);

                AppProfile app;
                if (!apps.TryGetValue(key, out app))
                {
                    app = new AppProfile { Name = FriendlyAppName(row.Name, row.Path), Path = row.Path, User = row.User };
                    apps[key] = app;
                }

                if (string.IsNullOrWhiteSpace(app.Path)) app.Path = row.Path;
                if (string.IsNullOrWhiteSpace(app.User)) app.User = row.User;
                app.Pids.Add(row.Pid);
                app.Cpu += row.Cpu;
                if (row.CpuSampleAvailable) app.CpuSampleCount++;
                app.PrivateMb += row.PrivateMb;
                app.RamMb += row.WorkingSetMb;
            }

            foreach (var row in network)
            {
                if (row.Pid == 0) continue;
                string key = !string.IsNullOrWhiteSpace(row.Path) ? row.Path : row.Process;
                if (string.IsNullOrWhiteSpace(key)) continue;

                AppProfile app;
                if (!apps.TryGetValue(key, out app))
                {
                    app = new AppProfile { Name = FriendlyAppName(row.Process, row.Path), Path = row.Path, User = row.User };
                    apps[key] = app;
                }

                if (string.IsNullOrWhiteSpace(app.Path)) app.Path = row.Path;
                if (string.IsNullOrWhiteSpace(app.User)) app.User = row.User;
                if (row.Pid > 0) app.Pids.Add(row.Pid);
                app.ConnectionCount++;
            }

            return apps.Values
                .OrderByDescending(a => a.ConnectionCount)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FriendlyAppName(string processName, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrWhiteSpace(file)) return file;
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(processName)) return "Unknown App";
            int titleSeparator = processName.IndexOf(" - ", StringComparison.Ordinal);
            return titleSeparator > 0 ? processName.Substring(0, titleSeparator) : processName;
        }

        private void FillAppGridFromCache()
        {
            FillAppGrid(AppProfilesForCurrentView());
        }

        private List<AppProfile> AppProfilesForCurrentView()
        {
            string filter = appSearchBox.Text.Trim();
            var apps = latestAppProfiles
                .Where(app => AppProfileMatchesFilter(app, filter, GetFirewallStatus(app.Path)))
                .ToList();
            Tuple<string, bool> sort;
            if (gridSortState.TryGetValue(appGrid, out sort)) apps = SortApps(apps, sort.Item1, sort.Item2);
            return apps;
        }

        internal static bool AppProfileMatchesFilter(AppProfile app, string filter, string firewallStatus)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (app == null) return false;

            string query = filter.Trim();
            return (app.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (app.Path ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (app.User ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (firewallStatus ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                app.Pids.Any(pid => pid.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                app.Pids.Count.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                app.ConnectionCount.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (app.CpuSampleCount > 0 && app.Cpu.ToString("0.0", CultureInfo.CurrentCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static string AppCpuDisplayText(AppProfile app)
        {
            return app == null || app.CpuSampleCount == 0 ? "..." : app.Cpu.ToString("0.0", CultureInfo.CurrentCulture);
        }

        private static string AppCpuSummaryText(AppProfile app)
        {
            if (app == null || app.CpuSampleCount == 0) return "sampling...";
            string value = app.Cpu.ToString("0.0", CultureInfo.CurrentCulture) + "%";
            return app.CpuSampleCount < app.Pids.Count
                ? value + " (" + app.CpuSampleCount.ToString(CultureInfo.CurrentCulture) + "/" + app.Pids.Count.ToString(CultureInfo.CurrentCulture) + " sampled)"
                : value;
        }

        private void FillAppGrid(List<AppProfile> apps)
        {
            string previousPath = null;
            if (appGrid.SelectedRows.Count > 0) previousPath = Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);

            updatingAppGrid = true;
            appGrid.SuspendLayout();
            try
            {
                appGrid.Rows.Clear();
                foreach (var app in apps)
                {
                    int index = appGrid.Rows.Add(app.Name, GetFirewallStatus(app.Path), app.Pids.Count, app.ConnectionCount,
                        AppCpuDisplayText(app), app.RamMb.ToString("0.0", CultureInfo.CurrentCulture), app.Path);
                    if (!string.IsNullOrWhiteSpace(previousPath) && string.Equals(previousPath, app.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        appGrid.Rows[index].Selected = true;
                    }
                }

                if (appGrid.Rows.Count > 0 && appGrid.SelectedRows.Count == 0) appGrid.Rows[0].Selected = true;
            }
            finally
            {
                appGrid.ResumeLayout();
                updatingAppGrid = false;
            }
        }

        private void ShowSelectedApp()
        {
            if (updatingAppGrid) return;
            if (appGrid.SelectedRows.Count == 0)
            {
                appTitleLabel.Text = "Select an app";
                appMetaLabel.Text = "";
                appConnectionCard.Text = "0\nGroup Connections";
                appMemoryCard.Text = "0 MB\nSum Private Bytes";
                appRamCard.Text = "0 MB\nSum Working Set";
                appFirewallCard.Text = "Unknown\nFirewall";
                appFirewallDetailsLabel.Text = "Select an app to inspect its Better Task Manager firewall rule.";
                appBlockButton.Enabled = false;
                appUnblockButton.Enabled = false;
                appViewProcessesButton.Enabled = false;
                appOpenFolderButton.Enabled = false;
                appCopyPathButton.Enabled = false;
                appConnectionsGrid.Rows.Clear();
                return;
            }

            AppProfile app = SelectedAppProfile();
            if (app == null) return;

            appTitleLabel.Text = app.Name;
            string pids = app.Pids.Count == 0 ? "No active PID" : "PID " + string.Join(", ", app.Pids.Take(8).Select(p => p.ToString(CultureInfo.InvariantCulture)));
            if (app.Pids.Count > 8) pids += " +" + (app.Pids.Count - 8).ToString(CultureInfo.InvariantCulture);
            appMetaLabel.Text = SnapshotLabel(latestAppsSnapshot) + "    " + app.Pids.Count.ToString(CultureInfo.CurrentCulture) + " processes aggregated    " + pids +
                "    CPU " + AppCpuSummaryText(app) + "    " +
                (string.IsNullOrWhiteSpace(app.User) ? "User unknown" : app.User) + "    " + (string.IsNullOrWhiteSpace(app.Path) ? "Path unavailable" : app.Path);
            appConnectionCard.Text = app.ConnectionCount.ToString(CultureInfo.InvariantCulture) + "\nGroup Connections";
            appMemoryCard.Text = app.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Private Bytes";
            appRamCard.Text = app.RamMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Working Set";
            string firewallStatus = GetFirewallStatus(app.Path);
            appFirewallCard.Text = firewallStatus + "\nFirewall";
            appFirewallDetailsLabel.Text = FirewallExplanation(app.Path, firewallStatus);
            appFirewallDetailsLabel.ForeColor = firewallStatus == FirewallStatusBlocked
                ? Theme.Danger
                : firewallStatus == FirewallStatusNoBlock ? Theme.MutedText : Theme.Warning;
            bool canChangeRule = isAdmin && !string.IsNullOrWhiteSpace(app.Path);
            appBlockButton.Enabled = canChangeRule && firewallStatus != FirewallStatusBlocked;
            appUnblockButton.Enabled = canChangeRule && firewallStatus == FirewallStatusBlocked;
            appViewProcessesButton.Enabled = app.Pids.Count > 0;
            bool hasExecutablePath = !string.IsNullOrWhiteSpace(app.Path);
            appOpenFolderButton.Enabled = hasExecutablePath && !string.IsNullOrWhiteSpace(ExecutableDirectory(app.Path));
            appCopyPathButton.Enabled = hasExecutablePath;

            appConnectionsGrid.SuspendLayout();
            try
            {
                appConnectionsGrid.Rows.Clear();
                var rows = latestNetworkRows.Where(r =>
                    (!string.IsNullOrWhiteSpace(app.Path) && string.Equals(r.Path, app.Path, StringComparison.OrdinalIgnoreCase)) ||
                    (string.IsNullOrWhiteSpace(app.Path) && string.Equals(r.Process, app.Name, StringComparison.OrdinalIgnoreCase)));

                foreach (var row in rows)
                {
                    string local = row.LocalAddress + (string.IsNullOrWhiteSpace(row.LocalPort) ? "" : ":" + row.LocalPort);
                    string remote = row.RemoteAddress + (string.IsNullOrWhiteSpace(row.RemotePort) ? "" : ":" + row.RemotePort);
                    appConnectionsGrid.Rows.Add(row.Protocol, local, remote, NormalizeConnectionState(row.State), NormalizeDisplayText(row.User), row.Path);
                }
            }
            finally
            {
                appConnectionsGrid.ResumeLayout();
            }
        }

        private AppProfile SelectedAppProfile()
        {
            if (appGrid.SelectedRows.Count == 0) return null;
            string name = Convert.ToString(appGrid.SelectedRows[0].Cells["App"].Value);
            string path = Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);
            return latestAppProfiles.FirstOrDefault(app =>
                (!string.IsNullOrWhiteSpace(path) && string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(path) && string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        private void ViewSelectedAppProcesses()
        {
            AppProfile app = SelectedAppProfile();
            if (app == null || app.Pids.Count == 0)
            {
                MessageBox.Show(this, "The selected app has no active process IDs in this snapshot.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            settingProcessFilter = true;
            try { filterBox.Text = app.Name; }
            finally { settingProcessFilter = false; }

            processPidScope = new HashSet<int>(app.Pids);
            List<ProcessRow> matchingRows = ProcessRowsForCurrentView();
            latestProcessSnapshot = latestAppsSnapshot;
            ShowPage(processTab);
            FillProcessGrid(matchingRows);
            statusLabel.Text = SnapshotLabel(latestProcessSnapshot) + "    Same Apps snapshot: " + app.Name + " (" + matchingRows.Count.ToString(CultureInfo.CurrentCulture) + " processes)";
            statusLabel.ForeColor = Theme.Info;
        }

        private async Task BlockSelectedAppAsync(bool block)
        {
            if (appGrid.SelectedRows.Count == 0) return;
            string path = Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "This app has no usable executable path, so a Windows Firewall app rule cannot be created.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!isAdmin)
            {
                MessageBox.Show(this, "Run as administrator to modify firewall rules.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string rule = RuleNameForPath(path);
            if (block)
            {
                if (MessageBox.Show(this, "Block outbound network access for:\n" + path, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var result = await Task.Run(() => CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + rule, "dir=out", "program=" + path, "action=block", "profile=any"));
                if (!result.Succeeded)
                {
                    ShowCommandFailure("Blocking outbound network access", result);
                    return;
                }
            }
            else
            {
                var result = await Task.Run(() => CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule));
                if (!result.Succeeded)
                {
                    ShowCommandFailure("Removing the firewall rule", result);
                    return;
                }
            }

            firewallStatusCache[path] = block ? FirewallStatusBlocked : FirewallStatusNoBlock;
            await RefreshAppsAsync(false);
        }

        private static void ApplyDarkTheme(Control root)
        {
            root.BackColor = Theme.Window;
            root.ForeColor = Theme.Text;

            foreach (Control control in root.Controls)
            {
                if (control is DataGridView)
                {
                    StyleGrid((DataGridView)control);
                }
                else if (control is ListView)
                {
                    control.BackColor = Theme.Surface;
                    control.ForeColor = Theme.Text;
                }
                else if (control is Button)
                {
                    var button = (Button)control;
                    button.FlatStyle = FlatStyle.Flat;
                    button.Cursor = Cursors.Hand;
                    button.BackColor = Theme.SurfaceAlt;
                    button.ForeColor = Theme.Text;
                    button.FlatAppearance.BorderColor = Theme.Border;
                    button.FlatAppearance.MouseOverBackColor = Theme.SurfaceRaised;
                    button.FlatAppearance.MouseDownBackColor = Theme.Accent;
                }
                else if (control is TextBox)
                {
                    ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
                    control.BackColor = Theme.SurfaceAlt;
                    control.ForeColor = Theme.Text;
                }
                else if (control is ComboBox)
                {
                    var comboBox = (ComboBox)control;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    comboBox.BackColor = Theme.SurfaceAlt;
                    comboBox.ForeColor = Theme.Text;
                }
                else if (control is CheckBox)
                {
                    control.BackColor = Theme.Window;
                    control.ForeColor = Theme.Text;
                }
                else
                {
                    control.BackColor = Theme.Window;
                    control.ForeColor = Theme.Text;
                }

                if (control is Label && ((Label)control).BorderStyle == BorderStyle.FixedSingle)
                {
                    control.BackColor = Theme.SurfaceAlt;
                    control.ForeColor = Theme.Text;
                }

                if (control.HasChildren) ApplyDarkTheme(control);
            }
        }

        private static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Theme.Window;
            grid.GridColor = Theme.Border;
            grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSelected;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceRaised;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceRaised;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.RowHeadersDefaultCellStyle.BackColor = Theme.SurfaceRaised;
            grid.RowHeadersDefaultCellStyle.ForeColor = Theme.Text;
            grid.RowsDefaultCellStyle.BackColor = Theme.Surface;
            grid.RowsDefaultCellStyle.ForeColor = Theme.Text;
        }

        private async Task RefreshProcessesAsync(bool automatic = false)
        {
            if (refreshingProcesses) return;
            refreshingProcesses = true;
            refreshButton.Enabled = false;
            statusLabel.Text = "Loading processes...";
            statusLabel.ForeColor = Theme.Warning;

            try
            {
                Dictionary<int, ProcessDetails> cache;
                lock (detailsCacheSync) cache = detailsCache;
                var rows = await RunSnapshotCollectionAsync(processTab, () => BuildProcessRows(cache));
                if (rows == null) return;
                latestProcessRows = rows;
                latestProcessSnapshot = DateTime.Now;
                processPidScope = null;
                FillProcessGridFromCache();
                statusLabel.Text = SnapshotLabel(latestProcessSnapshot) + "    " + (isAdmin
                    ? (detailsLoaded ? "Running as administrator - users/paths loaded" : "Running as administrator")
                    : "Not administrator: some actions may fail");
                statusLabel.ForeColor = isAdmin ? Theme.Good : Theme.Danger;
                MarkLiveRefreshSuccess();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Process refresh failed: " + ex.Message;
                statusLabel.ForeColor = Theme.Danger;
                MarkLiveRefreshFailure();
                if (ShouldShowRefreshDialog(automatic)) MessageBox.Show(this, ex.Message, "Process refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                refreshButton.Enabled = true;
                refreshingProcesses = false;
            }
        }

        private List<ProcessRow> BuildProcessRows(Dictionary<int, ProcessDetails> cache)
        {
            var now = DateTime.UtcNow;
            var result = new List<ProcessRow>();
            var activePids = new HashSet<int>();
            foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    int pid = process.Id;
                    activePids.Add(pid);
                    long processStartTimeUtcTicks = SafeProcessStartTimeUtcTicks(process);
                    ProcessDetails details = null;
                    lock (detailsCacheSync) cache.TryGetValue(pid, out details);
                    if (!CachedDetailsMatchProcessInstance(details, processStartTimeUtcTicks))
                    {
                        details = null;
                    }
                    string title = "";
                    try { title = process.MainWindowTitle; } catch { }
                    string name = string.IsNullOrWhiteSpace(title) ? process.ProcessName : process.ProcessName + " - " + title;
                    details = ResolveMissingProcessDetails(details, () => GetProcessPathFast(pid), () => GetProcessUserFast(pid));
                    details.ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
                    lock (detailsCacheSync) cache[pid] = details;
                    string user = details.User;
                    string path = details.Path;

                    double cpuPercent = 0;
                    bool cpuSampleAvailable = false;
                    try
                    {
                        TimeSpan totalCpu = process.TotalProcessorTime;
                        lock (cpuCacheSync)
                        {
                            Tuple<TimeSpan, DateTime, long> old;
                            if (lastCpu.TryGetValue(pid, out old) &&
                                (old.Item3 == 0 || processStartTimeUtcTicks == 0 || old.Item3 == processStartTimeUtcTicks))
                            {
                                double seconds = Math.Max(0.5, (now - old.Item2).TotalSeconds);
                                cpuPercent = Math.Max(0, Math.Min(100, Math.Round((totalCpu - old.Item1).TotalSeconds / (seconds * Environment.ProcessorCount) * 100, 1)));
                                cpuSampleAvailable = true;
                            }
                            lastCpu[pid] = Tuple.Create(totalCpu, now, processStartTimeUtcTicks);
                        }
                    }
                    catch { }

                    result.Add(new ProcessRow
                    {
                        Pid = pid,
                        Name = name,
                        User = user,
                        Cpu = cpuPercent,
                        CpuSampleAvailable = cpuSampleAvailable,
                        PrivateMb = ToMb(process.PrivateMemorySize64),
                        WorkingSetMb = ToMb(process.WorkingSet64),
                        PeakWorkingSetMb = ToMb(process.PeakWorkingSet64),
                        Threads = SafeThreadCount(process),
                        Path = path,
                        ProcessStartTimeUtcTicks = processStartTimeUtcTicks
                    });
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }

            lock (detailsCacheSync)
            {
                foreach (int stalePid in cache.Keys.Where(pid => !activePids.Contains(pid)).ToList()) cache.Remove(stalePid);
            }
            lock (cpuCacheSync)
            {
                foreach (int stalePid in lastCpu.Keys.Where(pid => !activePids.Contains(pid)).ToList()) lastCpu.Remove(stalePid);
            }
            return result;
        }

        internal static ProcessDetails ResolveMissingProcessDetails(ProcessDetails cached, Func<string> pathResolver, Func<string> userResolver)
        {
            string path = cached == null ? "" : cached.Path ?? "";
            string user = cached == null ? "" : cached.User ?? "";
            bool pathResolved = cached != null && cached.PathResolved;
            bool userResolved = cached != null && cached.UserResolved;

            if (!pathResolved)
            {
                path = pathResolver == null ? "" : pathResolver() ?? "";
                pathResolved = true;
            }
            if (!userResolved)
            {
                user = userResolver == null ? "" : userResolver() ?? "";
                userResolved = true;
            }

            return new ProcessDetails
            {
                Path = path,
                User = user,
                PathResolved = pathResolved,
                UserResolved = userResolved,
                ProcessStartTimeUtcTicks = cached == null ? 0 : cached.ProcessStartTimeUtcTicks
            };
        }

        internal static bool CachedDetailsMatchProcessInstance(ProcessDetails cached, long processStartTimeUtcTicks)
        {
            return cached == null || cached.ProcessStartTimeUtcTicks == 0 || processStartTimeUtcTicks == 0 ||
                cached.ProcessStartTimeUtcTicks == processStartTimeUtcTicks;
        }

        private void FillProcessGridFromCache()
        {
            FillProcessGrid(ProcessRowsForCurrentView());
        }

        private List<ProcessRow> ProcessRowsForCurrentView()
        {
            string filter = filterBox.Text.Trim();
            IEnumerable<ProcessRow> rows = latestProcessRows;
            if (processPidScope != null) rows = rows.Where(row => processPidScope.Contains(row.Pid));

            var result = rows.Where(row => ProcessRowMatchesFilter(row, filter)).ToList();
            Tuple<string, bool> sort;
            if (gridSortState.TryGetValue(processGrid, out sort))
            {
                result = SortProcesses(result, sort.Item1, sort.Item2);
            }
            return result;
        }

        internal static bool ProcessRowMatchesFilter(ProcessRow row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (row == null) return false;

            string query = filter.Trim();
            return row.Pid.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.User ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.Path ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FillProcessGrid(List<ProcessRow> rows)
        {
            int? selectedPid = SelectedPid(processGrid);
            int firstDisplayedRow = FirstDisplayedRow(processGrid);
            int selectedIndex = -1;
            processGrid.SuspendLayout();
            try
            {
                processGrid.Rows.Clear();
                foreach (var row in rows)
                {
                    int index = processGrid.Rows.Add(row.Pid, row.Name, NormalizeDisplayText(row.User), row.CpuSampleAvailable ? row.Cpu.ToString("0.0", CultureInfo.CurrentCulture) : "...",
                        row.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.WorkingSetMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.PeakWorkingSetMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.Threads, row.Path);
                    if (selectedPid == row.Pid) selectedIndex = index;
                }
            }
            finally
            {
                processGrid.ResumeLayout();
            }

            RestoreGridPosition(processGrid, selectedIndex, firstDisplayedRow);
            int sampledCpuRows = rows.Count(row => row.CpuSampleAvailable);
            string cpuSummary = sampledCpuRows == 0 && rows.Count > 0
                ? "sampling..."
                : rows.Where(row => row.CpuSampleAvailable).Sum(row => row.Cpu).ToString("0.0", CultureInfo.CurrentCulture) + "%" +
                    (sampledCpuRows < rows.Count ? " (" + sampledCpuRows.ToString(CultureInfo.CurrentCulture) + "/" + rows.Count.ToString(CultureInfo.CurrentCulture) + " sampled)" : "");
            processSummaryLabel.ForeColor = Theme.Info;
            processSummaryLabel.Text = "Visible rows: " + rows.Count.ToString(CultureInfo.CurrentCulture) +
                "    Sum CPU: " + cpuSummary +
                "    Sum Private Bytes: " + rows.Sum(row => row.PrivateMb).ToString("0.0", CultureInfo.CurrentCulture) + " MB" +
                "    Sum Working Set: " + rows.Sum(row => row.WorkingSetMb).ToString("0.0", CultureInfo.CurrentCulture) + " MB" +
                "    Working-set sums can overlap shared pages.";
        }

        private async Task LoadDetailsAndRefreshAsync()
        {
            if (refreshingProcesses) return;
            loadDetailsButton.Enabled = false;
            statusLabel.Text = "Loading usernames and full paths...";
            statusLabel.ForeColor = Theme.Warning;
            try
            {
                Dictionary<int, ProcessDetails> loadedDetails = await RunSnapshotCollectionAsync(processTab, () => LoadProcessDetails());
                if (loadedDetails == null) return;
                lock (detailsCacheSync) detailsCache = loadedDetails;
                detailsLoaded = true;
                await RefreshProcessesAsync();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Details lookup failed";
                statusLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, ex.Message, "Details lookup failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                loadDetailsButton.Enabled = true;
            }
        }

        private Dictionary<int, ProcessDetails> LoadProcessDetails()
        {
            var map = new Dictionary<int, ProcessDetails>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    int pid = process.Id;
                    map[pid] = new ProcessDetails
                    {
                        Path = GetProcessPathFast(pid),
                        User = GetProcessUserFast(pid),
                        PathResolved = true,
                        UserResolved = true,
                        ProcessStartTimeUtcTicks = SafeProcessStartTimeUtcTicks(process)
                    };
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
            return map;
        }

        private async Task RefreshNetworkAsync(bool automatic = false)
        {
            if (refreshingNetwork) return;
            refreshingNetwork = true;
            networkRefreshButton.Enabled = false;
            networkStatusLabel.Text = "Loading network connections...";
            networkStatusLabel.ForeColor = Theme.Warning;
            try
            {
                var rows = await RunSnapshotCollectionAsync(networkTab, () =>
                {
                    var networkRows = BuildNetworkRows();
                    SaveNetworkHistory(networkRows);
                    return networkRows;
                });
                if (rows == null) return;
                latestNetworkRows = rows;
                latestNetworkSnapshot = rows.Count > 0 ? rows[0].Timestamp : DateTime.Now;
                FillNetworkGridFromCache();
                UpdateBandwidthLabel();
                MarkLiveRefreshSuccess();
            }
            catch (Exception ex)
            {
                networkStatusLabel.Text = "Network refresh failed: " + ex.Message;
                networkStatusLabel.ForeColor = Theme.Danger;
                MarkLiveRefreshFailure();
                if (ShouldShowRefreshDialog(automatic)) MessageBox.Show(this, ex.Message, "Network refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                networkRefreshButton.Enabled = true;
                refreshingNetwork = false;
            }
        }

        private List<NetworkRow> BuildNetworkRows(IEnumerable<ProcessRow> knownProcessRows = null)
        {
            var now = DateTime.Now;
            var processIdentities = new Dictionary<int, Tuple<string, long>>();
            foreach (var p in Process.GetProcesses())
            {
                try { processIdentities[p.Id] = Tuple.Create(p.ProcessName, SafeProcessStartTimeUtcTicks(p)); } catch { }
                finally { p.Dispose(); }
            }

            var snapshotDetails = new Dictionary<int, ProcessDetails>();
            lock (detailsCacheSync)
            {
                foreach (var pair in detailsCache) snapshotDetails[pair.Key] = pair.Value;
            }
            if (knownProcessRows != null)
            {
                foreach (ProcessRow processRow in knownProcessRows)
                {
                    snapshotDetails[processRow.Pid] = new ProcessDetails
                    {
                        Path = processRow.Path,
                        User = processRow.User,
                        PathResolved = true,
                        UserResolved = true,
                        ProcessStartTimeUtcTicks = processRow.ProcessStartTimeUtcTicks
                    };
                }
            }

            var rows = new List<NetworkRow>();
            foreach (var connection in NativeNetworkCollector.GetAll())
            {
                ProcessDetails details = null;
                snapshotDetails.TryGetValue(connection.OwningPid, out details);
                Tuple<string, long> identity;
                processIdentities.TryGetValue(connection.OwningPid, out identity);
                string name = identity == null ? "" : identity.Item1;
                long processStartTimeUtcTicks = identity == null ? 0 : identity.Item2;
                if (!CachedDetailsMatchProcessInstance(details, processStartTimeUtcTicks))
                {
                    details = null;
                }
                int owningPid = connection.OwningPid;
                details = ResolveMissingProcessDetails(details, () => GetProcessPathFast(owningPid), () => GetProcessUserFast(owningPid));
                details.ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
                snapshotDetails[owningPid] = details;
                lock (detailsCacheSync) detailsCache[owningPid] = details;
                string path = details.Path;
                string user = details.User;

                rows.Add(new NetworkRow
                {
                    Timestamp = now,
                    Process = name,
                    Pid = connection.OwningPid,
                    User = NormalizeDisplayText(user),
                    Protocol = connection.Protocol,
                    LocalAddress = connection.LocalAddress,
                    LocalPort = connection.LocalPort.ToString(CultureInfo.InvariantCulture),
                    RemoteAddress = connection.RemoteAddress,
                    RemotePort = connection.Protocol == "UDP" ? "" : connection.RemotePort.ToString(CultureInfo.InvariantCulture),
                    State = connection.State,
                    Path = path
                });
            }
            return rows.OrderBy(r => r.Process).ThenBy(r => r.Protocol).ThenBy(r => r.RemoteAddress).ToList();
        }

        private void FillNetworkGridFromCache()
        {
            List<NetworkRow> rows = NetworkRowsForCurrentView();
            FillNetworkGrid(rows);
            networkStatusLabel.Text = SnapshotLabel(latestNetworkSnapshot) + "    " +
                rows.Count.ToString(CultureInfo.CurrentCulture) + "/" + latestNetworkRows.Count.ToString(CultureInfo.CurrentCulture) +
                " connections shown. Per-app bandwidth needs ETW/WFP collector.";
            networkStatusLabel.ForeColor = Theme.MutedText;
        }

        private List<NetworkRow> NetworkRowsForCurrentView()
        {
            string filter = networkFilterBox.Text.Trim();
            var result = latestNetworkRows.Where(row => NetworkRowMatchesFilter(row, filter)).ToList();
            Tuple<string, bool> sort;
            if (gridSortState.TryGetValue(networkGrid, out sort))
            {
                result = SortNetworkRows(result, sort.Item1, sort.Item2);
            }
            return result;
        }

        internal static bool NetworkRowMatchesFilter(NetworkRow row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (row == null) return false;

            string query = filter.Trim();
            return row.Pid.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.Process ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.User ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.Protocol ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.LocalAddress ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.LocalPort ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.RemoteAddress ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.RemotePort ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                NormalizeConnectionState(row.State).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (row.Path ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FillNetworkGrid(List<NetworkRow> rows)
        {
            string selectedKey = SelectedNetworkKey();
            int firstDisplayedRow = FirstDisplayedRow(networkGrid);
            int selectedIndex = -1;
            networkGrid.SuspendLayout();
            try
            {
                networkGrid.Rows.Clear();
                foreach (var row in rows)
                {
                    int index = networkGrid.Rows.Add(row.Process, row.Pid, NormalizeDisplayText(row.User), row.Protocol, row.LocalAddress, row.LocalPort,
                        row.RemoteAddress, row.RemotePort, NormalizeConnectionState(row.State), row.Path);
                    if (selectedKey == NetworkKey(row.Pid, row.Protocol, row.LocalAddress, row.LocalPort, row.RemoteAddress, row.RemotePort)) selectedIndex = index;
                }
            }
            finally
            {
                networkGrid.ResumeLayout();
            }

            RestoreGridPosition(networkGrid, selectedIndex, firstDisplayedRow);
        }

        private async Task KillSelectedAsync()
        {
            int? pid = SelectedPid(processGrid);
            if (pid == null)
            {
                MessageBox.Show(this, "Select a process first.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Force kill PID " + pid.Value + " and its child processes?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                await Task.Run(() =>
                {
                    using (var process = Process.GetProcessById(pid.Value))
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                });
                await RefreshProcessesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Force-killing PID " + pid.Value + " failed.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task TrimSelectedAsync()
        {
            int? pid = SelectedPid(processGrid);
            if (pid == null)
            {
                MessageBox.Show(this, "Select a process first.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await Task.Run(() =>
            {
                using (var process = Process.GetProcessById(pid.Value))
                {
                    NativeMethods.EmptyWorkingSet(process.Handle);
                }
            });
            await RefreshProcessesAsync();
        }

        private async Task TrimAllAsync()
        {
            if (MessageBox.Show(this, "Trim memory for all accessible apps? This can reduce visible RAM use but apps may reload data afterward.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            trimAllButton.Enabled = false;
            int count = await Task.Run(() =>
            {
                int trimmed = 0;
                foreach (var process in Process.GetProcesses())
                {
                    try { if (NativeMethods.EmptyWorkingSet(process.Handle)) trimmed++; }
                    catch { }
                    finally { process.Dispose(); }
                }
                return trimmed;
            });
            trimAllButton.Enabled = true;
            memoryStatusLabel.Text = "Trimmed working sets for " + count + " processes.";
            await RefreshProcessesAsync();
            RefreshMemoryPage();
        }

        private bool RefreshMemoryPage()
        {
            try
            {
                SystemCpuSnapshot cpuSnapshot = systemCpuCollector.GetSnapshot();
                SystemMemorySnapshot snapshot = NativeMemoryCollector.GetSnapshot();
                memoryCpuCard.Text = (cpuSnapshot.SampleAvailable ? cpuSnapshot.UsagePercent.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "...") + "\nSystem CPU";
                memoryCpuCard.ForeColor = !cpuSnapshot.SampleAvailable ? Theme.MutedText : cpuSnapshot.UsagePercent >= 90 ? Theme.Danger : cpuSnapshot.UsagePercent >= 75 ? Theme.Warning : Theme.Good;
                memoryLoadCard.Text = snapshot.PhysicalLoadPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%\nPhysical Load";
                memoryLoadCard.ForeColor = snapshot.PhysicalLoadPercent >= 90 ? Theme.Danger : snapshot.PhysicalLoadPercent >= 75 ? Theme.Warning : Theme.Good;
                memoryUsedCard.Text = FormatGiB(snapshot.PhysicalUsedBytes) + "\nUsed RAM";
                memoryAvailableCard.Text = FormatGiB(snapshot.PhysicalAvailableBytes) + "\nAvailable RAM";
                memoryCommitCard.Text = FormatGiB(snapshot.CommitTotalBytes) + " / " + FormatGiB(snapshot.CommitLimitBytes) + "\nCommit / Limit";
                memoryCacheCard.Text = FormatGiB(snapshot.SystemCacheBytes) + "\nSystem Cache";
                memorySnapshotLabel.ForeColor = Theme.MutedText;
                memorySnapshotLabel.Text = SnapshotLabel(snapshot.Timestamp) +
                    "    Total RAM " + FormatGiB(snapshot.PhysicalTotalBytes) +
                    "    Commit peak " + FormatGiB(snapshot.CommitPeakBytes) +
                    "    Processes " + snapshot.ProcessCount.ToString(CultureInfo.CurrentCulture) +
                    "    Threads " + snapshot.ThreadCount.ToString(CultureInfo.CurrentCulture) +
                    "    Handles " + snapshot.HandleCount.ToString(CultureInfo.CurrentCulture);
                return true;
            }
            catch (Exception ex)
            {
                memorySnapshotLabel.ForeColor = Theme.Danger;
                memorySnapshotLabel.Text = "Memory snapshot failed: " + ex.Message;
                return false;
            }
        }

        private static string FormatGiB(ulong bytes)
        {
            return (bytes / 1024d / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " GiB";
        }

        private void ClearStandby()
        {
            if (!isAdmin)
            {
                MessageBox.Show(this, "Run as administrator to clear standby cache.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Clear Windows standby cache?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int result = NativeMethods.PurgeStandbyList();
            memoryStatusLabel.Text = "Clear standby cache: " + NativeMemoryResultText(result);
            RefreshMemoryPage();
        }

        private void EmptySystemWorkingSets()
        {
            if (!isAdmin)
            {
                MessageBox.Show(this, "Run as administrator to use this system-level memory action.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Release system cache/working sets? Use this only for troubleshooting memory pressure.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int result = NativeMethods.EmptySystemWorkingSets();
            memoryStatusLabel.Text = "System working set cleanup: " + NativeMemoryResultText(result);
            RefreshMemoryPage();
        }

        private async Task BlockSelectedAsync(bool block)
        {
            if (!isAdmin)
            {
                MessageBox.Show(this, "Run as administrator to modify firewall rules.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = SelectedNetworkPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "Select a network row with an application path. Press Load Users/Paths first if paths are blank.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string rule = RuleNameForPath(path);
            if (block)
            {
                if (MessageBox.Show(this, "Block outbound network access for:\n" + path, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var result = await Task.Run(() => CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + rule, "dir=out", "program=" + path, "action=block", "profile=any"));
                if (!result.Succeeded)
                {
                    ShowCommandFailure("Blocking outbound network access", result);
                    return;
                }
                MessageBox.Show(this, "Blocked outbound network access for this app.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var result = await Task.Run(() => CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule));
                if (!result.Succeeded)
                {
                    ShowCommandFailure("Removing the firewall rule", result);
                    return;
                }
                MessageBox.Show(this, "Removed this app's Better Task Manager block rule.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            firewallStatusCache[path] = block ? FirewallStatusBlocked : FirewallStatusNoBlock;
        }

        private async Task ShowHistoryAsync()
        {
            ShowPage(historyTab);
            if (liveMonitoringCheck.Checked) await RefreshHistoryLiveAsync();
            else await LoadHistoryGridAsync();
        }

        private async Task LoadHistoryGridAsync()
        {
            if (loadingHistory || refreshingHistory) return;
            loadingHistory = true;
            reloadHistoryButton.Enabled = false;
            clearHistoryButton.Enabled = false;
            historyNoteLabel.Text = "Loading recent connection changes...";
            try
            {
                latestHistoryRows = await Task.Run(() => historyStore.LoadRecent(2000));
                FillHistoryGrid(false);
            }
            catch (Exception ex)
            {
                historyNoteLabel.Text = "History load failed: " + ex.Message;
                historyNoteLabel.ForeColor = Theme.Danger;
            }
            finally
            {
                reloadHistoryButton.Enabled = true;
                clearHistoryButton.Enabled = true;
                loadingHistory = false;
            }
        }

        private async Task ClearHistoryAsync()
        {
            if (loadingHistory || refreshingHistory) return;
            if (MessageBox.Show(this,
                "Clear all saved Better Task Manager connection history?\n\nThis cannot be undone. Live monitoring can record new changes again after clearing.",
                "Clear connection history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            loadingHistory = true;
            reloadHistoryButton.Enabled = false;
            clearHistoryButton.Enabled = false;
            historyNoteLabel.Text = "Clearing saved connection history...";
            historyNoteLabel.ForeColor = Theme.Warning;
            try
            {
                await Task.Run(() => historyStore.Clear());
                latestHistoryRows = new List<string[]>();
                visibleHistoryRows = new List<string[]>();
                historyPageStart = 0;
                FillHistoryGrid(true);
                historyNoteLabel.Text = "Connection history cleared. Live monitoring can record new changes.";
                historyNoteLabel.ForeColor = Theme.Good;
            }
            catch (Exception ex)
            {
                historyNoteLabel.Text = "Clearing History failed: " + ex.Message;
                historyNoteLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, ex.Message, "Clear History failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                reloadHistoryButton.Enabled = true;
                clearHistoryButton.Enabled = true;
                loadingHistory = false;
            }
        }

        private async Task RefreshHistoryLiveAsync()
        {
            if (refreshingHistory || loadingHistory) return;
            refreshingHistory = true;
            reloadHistoryButton.Enabled = false;
            clearHistoryButton.Enabled = false;
            historyNoteLabel.Text = "Sampling active connections...";
            historyNoteLabel.ForeColor = Theme.Warning;
            try
            {
                var result = await RunSnapshotCollectionAsync(historyTab, () =>
                {
                    List<NetworkRow> connections = BuildNetworkRows();
                    DateTime sampledAt = connections.Count > 0 ? connections[0].Timestamp : DateTime.Now;
                    int recorded = historyStore.SaveSnapshot(connections, sampledAt);
                    List<string[]> history = historyStore.LoadRecent(2000);
                    return Tuple.Create(history, connections.Count, recorded, sampledAt);
                });
                if (result == null) return;

                latestHistoryRows = result.Item1;
                FillHistoryGrid(false);
                historyNoteLabel.Text = "Live " + result.Item4.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + ": " +
                    result.Item2.ToString(CultureInfo.CurrentCulture) + " active, " +
                    result.Item3.ToString(CultureInfo.CurrentCulture) + " recorded. " + historyNoteLabel.Text;
                MarkLiveRefreshSuccess();
            }
            catch (Exception ex)
            {
                historyNoteLabel.Text = "Live History refresh failed: " + ex.Message;
                historyNoteLabel.ForeColor = Theme.Danger;
                MarkLiveRefreshFailure();
            }
            finally
            {
                reloadHistoryButton.Enabled = true;
                clearHistoryButton.Enabled = true;
                refreshingHistory = false;
            }
        }

        private void FillHistoryGrid(bool resetPage = true)
        {
            string filter = historyFilterBox.Text.Trim();
            visibleHistoryRows = latestHistoryRows.Where(row => HistoryRowMatchesFilter(row, filter)).ToList();

            SortHistoryRows();
            if (resetPage) historyPageStart = 0;
            UpdateHistoryPage();
        }

        private void MoveHistoryPage(int direction)
        {
            if (direction == 0 || visibleHistoryRows.Count == 0) return;
            historyPageStart += direction * HistoryDisplayLimit;
            UpdateHistoryPage();
        }

        private void UpdateHistoryPage()
        {
            int maximumStart = visibleHistoryRows.Count == 0 ? 0 : ((visibleHistoryRows.Count - 1) / HistoryDisplayLimit) * HistoryDisplayLimit;
            historyPageStart = Math.Max(0, Math.Min(historyPageStart, maximumStart));
            int displayed = Math.Min(HistoryDisplayLimit, Math.Max(0, visibleHistoryRows.Count - historyPageStart));
            historyList.VirtualListSize = displayed;
            historyPreviousButton.Enabled = historyPageStart > 0;
            historyNextButton.Enabled = historyPageStart + displayed < visibleHistoryRows.Count;
            historyList.Invalidate();

            historyNoteLabel.ForeColor = Theme.MutedText;
            string scope = latestHistoryRows.Count == 2000 ? "2,000 retained" : latestHistoryRows.Count.ToString(CultureInfo.CurrentCulture) + " retained";
            if (displayed == 0)
            {
                historyNoteLabel.Text = "0 matches (" + scope + ").";
            }
            else
            {
                int first = historyPageStart + 1;
                int last = historyPageStart + displayed;
                historyNoteLabel.Text = first.ToString(CultureInfo.CurrentCulture) + "-" + last.ToString(CultureInfo.CurrentCulture) +
                    "/" + visibleHistoryRows.Count.ToString(CultureInfo.CurrentCulture) + " matches (" + scope + "); export includes all matches.";
            }
        }

        private void HistoryListRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            int sourceIndex = historyPageStart + e.ItemIndex;
            if (e.ItemIndex < 0 || sourceIndex < 0 || sourceIndex >= visibleHistoryRows.Count)
            {
                e.Item = new ListViewItem("");
                return;
            }

            string[] row = visibleHistoryRows[sourceIndex];
            var item = new ListViewItem(row.Length > 0 ? row[0] : "");
            for (int index = 1; index < historyList.Columns.Count; index++)
            {
                item.SubItems.Add(index < row.Length ? row[index] : "");
            }
            e.Item = item;
        }

        internal static bool HistoryRowMatchesFilter(string[] row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (row == null) return false;
            return row.Any(value => (value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal async Task RunUiSmokeTestAsync()
        {
            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                throw new InvalidOperationException("WinForms did not start in PerMonitorV2 high-DPI mode.");
            }
            await ShowHistoryAsync();
            if (historyList.VirtualListSize != Math.Min(latestHistoryRows.Count, HistoryDisplayLimit))
            {
                throw new InvalidOperationException("History view count does not match the loaded history cache.");
            }
            if (!clearHistoryButton.Enabled)
            {
                throw new InvalidOperationException("Clear History action did not return to its idle state after loading.");
            }

            historyFilterBox.Text = "better-task-manager-no-match-probe";
            if (historyList.VirtualListSize != 0)
            {
                throw new InvalidOperationException("History filter did not update the visible row count.");
            }

            historyFilterBox.Clear();
            if (historyList.VirtualListSize != Math.Min(latestHistoryRows.Count, HistoryDisplayLimit))
            {
                throw new InvalidOperationException("History view did not restore its rows after clearing the filter.");
            }

            ShowPage(historyTab);
            await RefreshActivePageAsync();
            if (!historyNoteLabel.Text.StartsWith("Live ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Live monitoring did not sample the active History page.");
            }

            latestHistoryRows = Enumerable.Range(0, 250)
                .Select(index => new[]
                {
                    "2026-01-01T00:00:00",
                    "item-" + index.ToString("000", CultureInfo.InvariantCulture),
                    index.ToString(CultureInfo.InvariantCulture),
                    "TEST\\User",
                    "TCP",
                    "127.0.0.1",
                    "5000",
                    "10.0.0.1",
                    "443",
                    "Established",
                    "C:\\Apps\\item.exe"
                })
                .ToList();
            FillHistoryGrid(true);
            if (historyPageStart != 0 || historyList.VirtualListSize != 100 || historyPreviousButton.Enabled || !historyNextButton.Enabled)
            {
                throw new InvalidOperationException("History paging did not initialize the first page correctly.");
            }

            if (!await HandleGlobalShortcutAsync(Keys.PageDown)) throw new InvalidOperationException("Page Down was not handled on History.");
            var secondPageItem = new RetrieveVirtualItemEventArgs(0);
            HistoryListRetrieveVirtualItem(historyList, secondPageItem);
            if (historyPageStart != 100 || secondPageItem.Item.SubItems[1].Text != "item-100")
            {
                throw new InvalidOperationException("History paging did not expose the second page correctly.");
            }
            FillHistoryGrid(false);
            if (historyPageStart != 100)
            {
                throw new InvalidOperationException("History reload did not preserve the current result page.");
            }

            await HandleGlobalShortcutAsync(Keys.PageDown);
            if (historyPageStart != 200 || historyList.VirtualListSize != 50 || !historyPreviousButton.Enabled || historyNextButton.Enabled)
            {
                throw new InvalidOperationException("History paging did not clamp the final page correctly.");
            }
            await HandleGlobalShortcutAsync(Keys.PageUp);
            if (historyPageStart != 100) throw new InvalidOperationException("Page Up did not return to the previous History page.");

            historyFilterBox.Text = "item-249";
            if (historyPageStart != 0 || historyList.VirtualListSize != 1)
            {
                throw new InvalidOperationException("History filtering did not reset paging to the first result page.");
            }
            historyFilterBox.Clear();

            latestProcessRows = new List<ProcessRow>
            {
                new ProcessRow { Pid = 101, Name = "alpha", User = "TEST\\One", Path = "C:\\Apps\\alpha.exe", PrivateMb = 10, WorkingSetMb = 20 },
                new ProcessRow { Pid = 202, Name = "beta", User = "TEST\\Two", Path = "C:\\Apps\\beta.exe", PrivateMb = 30, WorkingSetMb = 40 }
            };
            ShowPage(processTab);
            refreshingProcesses = true;
            try
            {
                filterBox.Text = "202";
                if (processGrid.Rows.Count != 1 || Convert.ToInt32(processGrid.Rows[0].Cells["PID"].Value, CultureInfo.InvariantCulture) != 202)
                {
                    throw new InvalidOperationException("Process search did not filter the cached snapshot by PID.");
                }
                if (Convert.ToString(processGrid.Rows[0].Cells["CPU"].Value) != "...")
                {
                    throw new InvalidOperationException("First-snapshot Process CPU did not show its sampling state.");
                }
                filterBox.Clear();
            }
            finally
            {
                refreshingProcesses = false;
            }

            gridSortState[processGrid] = Tuple.Create("PID", false);
            FillProcessGridFromCache();
            if (processGrid.Rows.Count != 2 || Convert.ToInt32(processGrid.Rows[0].Cells["PID"].Value, CultureInfo.InvariantCulture) != 202)
            {
                throw new InvalidOperationException("Process sorting was not preserved while filtering the cached snapshot.");
            }

            processPidScope = new HashSet<int> { 101 };
            FillProcessGridFromCache();
            if (processGrid.Rows.Count != 1 || Convert.ToInt32(processGrid.Rows[0].Cells["PID"].Value, CultureInfo.InvariantCulture) != 101)
            {
                throw new InvalidOperationException("Same-snapshot app PID scope was not preserved in the Process view.");
            }
            if (!processCopyPathButton.Enabled || !processOpenFolderButton.Enabled)
            {
                throw new InvalidOperationException("Process executable path actions did not follow selection state.");
            }

            latestNetworkRows = new List<NetworkRow>
            {
                new NetworkRow { Pid = 101, Process = "alpha", User = "TEST\\One", Protocol = "TCP", LocalAddress = "127.0.0.1", LocalPort = "5000", RemoteAddress = "10.0.0.1", RemotePort = "443", State = "Established", Path = "C:\\Apps\\alpha.exe" },
                new NetworkRow { Pid = 202, Process = "beta", User = "TEST\\Two", Protocol = "UDP", LocalAddress = "0.0.0.0", LocalPort = "5353", RemoteAddress = "*", RemotePort = "", State = "Listening", Path = "C:\\Apps\\beta.exe" }
            };
            latestNetworkSnapshot = DateTime.Now;
            ShowPage(networkTab);
            networkFilterBox.Text = "443";
            if (networkGrid.Rows.Count != 1 || Convert.ToInt32(networkGrid.Rows[0].Cells["PID"].Value, CultureInfo.InvariantCulture) != 101)
            {
                throw new InvalidOperationException("Network search did not filter the cached snapshot across endpoint fields.");
            }

            networkFilterBox.Clear();
            gridSortState[networkGrid] = Tuple.Create("PID", false);
            FillNetworkGridFromCache();
            if (networkGrid.Rows.Count != 2 || Convert.ToInt32(networkGrid.Rows[0].Cells["PID"].Value, CultureInfo.InvariantCulture) != 202)
            {
                throw new InvalidOperationException("Network sorting was not preserved for the cached snapshot.");
            }
            if (!networkCopyPathButton.Enabled || !networkOpenFolderButton.Enabled)
            {
                throw new InvalidOperationException("Network executable path actions did not follow selection state.");
            }
            networkFilterBox.Text = "443";
            if (!await HandleGlobalShortcutAsync(Keys.Escape) || networkFilterBox.Text.Length != 0)
            {
                throw new InvalidOperationException("Escape did not clear the active Network filter.");
            }
            if (!await HandleGlobalShortcutAsync(Keys.Control | Keys.F) || !networkFilterBox.Focused)
            {
                throw new InvalidOperationException("Ctrl+F did not focus the active Network filter.");
            }

            var alphaApp = new AppProfile { Name = "alpha", Path = "C:\\Apps\\alpha.exe", User = "TEST\\One", ConnectionCount = 1, Cpu = 1.5, CpuSampleCount = 1, RamMb = 20 };
            alphaApp.Pids.Add(101);
            var betaApp = new AppProfile { Name = "beta", Path = "C:\\Apps\\beta.exe", User = "TEST\\Two", ConnectionCount = 5, Cpu = 7.5, CpuSampleCount = 1, RamMb = 40 };
            betaApp.Pids.Add(202);
            latestAppProfiles = new List<AppProfile> { alphaApp, betaApp };
            firewallStatusCache[alphaApp.Path] = FirewallStatusNoBlock;
            firewallStatusCache[betaApp.Path] = FirewallStatusBlocked;
            ShowPage(appsTab);
            appSearchBox.Text = "alpha";
            if (appGrid.Rows.Count != 1 || Convert.ToString(appGrid.Rows[0].Cells["App"].Value) != "alpha")
            {
                throw new InvalidOperationException("Apps search did not filter the cached snapshot.");
            }

            appSearchBox.Clear();
            gridSortState[appGrid] = Tuple.Create("Connections", false);
            FillAppGridFromCache();
            if (appGrid.Rows.Count != 2 || Convert.ToString(appGrid.Rows[0].Cells["App"].Value) != "beta")
            {
                throw new InvalidOperationException("Apps sorting was not preserved for the cached snapshot.");
            }
            string selectedAppPath = appGrid.SelectedRows.Count == 0 ? "" : Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);
            if (!string.Equals(selectedAppPath, alphaApp.Path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Apps selection was not preserved through filtering and sorting.");
            }
            if (!appCopyPathButton.Enabled || !appOpenFolderButton.Enabled)
            {
                throw new InvalidOperationException("Apps executable path actions did not follow selection state.");
            }
            gridSortState[appGrid] = Tuple.Create("Cpu", false);
            FillAppGridFromCache();
            if (Convert.ToString(appGrid.Rows[0].Cells["App"].Value) != "beta" ||
                Convert.ToString(appGrid.Rows[0].Cells["Cpu"].Value) != betaApp.Cpu.ToString("0.0", CultureInfo.CurrentCulture))
            {
                throw new InvalidOperationException("Grouped Apps CPU was not rendered or sorted numerically.");
            }

            await VerifySnapshotCollectionGateAsync();
            ShowPage(memoryTab);
            if (!RefreshMemoryPage() || !memoryCpuCard.Text.StartsWith("...", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Memory dashboard did not expose the initial System CPU sampling state.");
            }
            VerifyNarrowLayout();
            if (!await HandleGlobalShortcutAsync(Keys.Control | Keys.D5) || activePage != memoryTab)
            {
                throw new InvalidOperationException("Ctrl+5 did not navigate to the Memory page.");
            }
            refreshIntervalBox.SelectedIndex = 3;
            networkGrid.Columns["Process"].Width = 222;
            SaveAppSettings();
            AppSettings savedUiSettings = settingsStore.Load();
            int savedNetworkWidth;
            if (savedUiSettings.RefreshIntervalIndex != 3 || savedUiSettings.ColumnWidths == null ||
                !savedUiSettings.ColumnWidths.TryGetValue("Network.Process", out savedNetworkWidth) || savedNetworkWidth != 222)
            {
                throw new InvalidOperationException("Main window did not persist Live interval and column width preferences.");
            }
        }

        private void VerifyNarrowLayout()
        {
            WindowState = FormWindowState.Normal;
            Size = MinimumSize;
            PerformLayout();
            navBar.PerformLayout();
            if (!VisibleFlowChildrenFit(navBar)) throw new InvalidOperationException("Global navigation clipped controls at minimum window width.");

            ShowPage(appsTab);
            PerformLayout();
            appMetricCards.PerformLayout();
            appActions.PerformLayout();
            if (!VisibleFlowChildrenFit(appMetricCards) || !VisibleFlowChildrenFit(appActions))
            {
                throw new InvalidOperationException("Apps cards or actions clipped controls at minimum window width.");
            }

            ShowPage(processTab);
            PerformLayout();
            processToolbar.PerformLayout();
            if (!VisibleFlowChildrenFit(processToolbar)) throw new InvalidOperationException("Process toolbar clipped controls at minimum window width.");

            ShowPage(networkTab);
            PerformLayout();
            networkToolbar.PerformLayout();
            if (!VisibleFlowChildrenFit(networkToolbar)) throw new InvalidOperationException("Network toolbar clipped controls at minimum window width.");

            ShowPage(historyTab);
            PerformLayout();
            historyToolbar.PerformLayout();
            if (!VisibleFlowChildrenFit(historyToolbar)) throw new InvalidOperationException("History toolbar clipped controls at minimum window width.");
        }

        internal static bool VisibleFlowChildrenFit(FlowLayoutPanel panel)
        {
            if (panel == null || panel.ClientSize.Width <= 0 || panel.ClientSize.Height <= 0) return false;
            return panel.Controls.Cast<Control>()
                .Where(control => control.Visible)
                .All(control => control.Left >= 0 && control.Top >= 0 && control.Right <= panel.ClientSize.Width && control.Bottom <= panel.ClientSize.Height);
        }

        private async Task VerifySnapshotCollectionGateAsync()
        {
            ShowPage(appsTab);
            var collectorStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var releaseCollector = new System.Threading.ManualResetEventSlim(false))
            {
                int staleCollectorCalls = 0;
                int currentCollectorCalls = 0;
                Task<string> first = RunSnapshotCollectionAsync(appsTab, () =>
                {
                    collectorStarted.TrySetResult(true);
                    releaseCollector.Wait(TimeSpan.FromSeconds(2));
                    return "apps";
                });

                await collectorStarted.Task;
                ShowPage(networkTab);
                Task<string> stale = RunSnapshotCollectionAsync(processTab, () =>
                {
                    staleCollectorCalls++;
                    return "processes";
                });
                Task<string> current = RunSnapshotCollectionAsync(networkTab, () =>
                {
                    currentCollectorCalls++;
                    return "network";
                });
                releaseCollector.Set();

                string[] results = await Task.WhenAll(first, stale, current);
                if (results[0] != null || results[1] != null || results[2] != "network" || staleCollectorCalls != 0 || currentCollectorCalls != 1)
                {
                    throw new InvalidOperationException("Snapshot collection gate did not suppress stale cross-page work.");
                }
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Verb = "runas",
                    UseShellExecute = true
                };
                if (Process.Start(psi) != null) Close();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                statusLabel.Text = "Administrator restart was cancelled.";
                statusLabel.ForeColor = Theme.Warning;
                adminStatusLabel.Text = "Elevation cancelled";
                adminStatusLabel.ForeColor = Theme.Warning;
            }
            catch (Exception ex)
            {
                adminStatusLabel.Text = "Elevation failed";
                adminStatusLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, "Could not restart as administrator.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private int? SelectedPid(DataGridView grid)
        {
            if (grid.SelectedRows.Count == 0) return null;
            object value = grid.SelectedRows[0].Cells["PID"].Value;
            int pid;
            return int.TryParse(Convert.ToString(value), out pid) ? (int?)pid : null;
        }

        private string SelectedExecutablePath()
        {
            if (activePage == appsTab)
            {
                AppProfile app = SelectedAppProfile();
                return app == null ? "" : app.Path ?? "";
            }
            if (activePage == processTab) return SelectedGridPath(processGrid);
            if (activePage == networkTab) return SelectedGridPath(networkGrid);
            return "";
        }

        private static string SelectedGridPath(DataGridView grid)
        {
            if (grid == null || grid.SelectedRows.Count == 0 || !grid.Columns.Contains("Path")) return "";
            return Convert.ToString(grid.SelectedRows[0].Cells["Path"].Value) ?? "";
        }

        internal static string ExecutableDirectory(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return "";
            try { return Path.GetDirectoryName(executablePath) ?? ""; }
            catch (ArgumentException) { return ""; }
            catch (NotSupportedException) { return ""; }
        }

        private void UpdateExecutablePathActions()
        {
            string processPath = SelectedGridPath(processGrid);
            processCopyPathButton.Enabled = !string.IsNullOrWhiteSpace(processPath);
            processOpenFolderButton.Enabled = !string.IsNullOrWhiteSpace(ExecutableDirectory(processPath));

            string networkPath = SelectedGridPath(networkGrid);
            networkCopyPathButton.Enabled = !string.IsNullOrWhiteSpace(networkPath);
            networkOpenFolderButton.Enabled = !string.IsNullOrWhiteSpace(ExecutableDirectory(networkPath));
        }

        private void OpenSelectedExecutableFolder()
        {
            string path = SelectedExecutablePath();
            string folder = ExecutableDirectory(path);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "The selected executable folder is unavailable.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                SetPathActionStatus("Opened folder: " + folder, Theme.Good);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the executable folder.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task CopySelectedExecutablePathAsync()
        {
            string path = SelectedExecutablePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "The selected row has no executable path.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(path);
                    SetPathActionStatus("Copied executable path.", Theme.Good);
                    return;
                }
                catch (ExternalException) when (attempt < 2)
                {
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy the executable path.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            MessageBox.Show(this, "The Windows clipboard remained busy. Try again.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetPathActionStatus(string text, Color color)
        {
            if (activePage == appsTab)
            {
                appFirewallDetailsLabel.Text = text;
                appFirewallDetailsLabel.ForeColor = color;
            }
            else if (activePage == processTab)
            {
                statusLabel.Text = text;
                statusLabel.ForeColor = color;
            }
            else if (activePage == networkTab)
            {
                networkStatusLabel.Text = text;
                networkStatusLabel.ForeColor = color;
            }
        }

        private string SelectedNetworkPath()
        {
            if (networkGrid.SelectedRows.Count == 0) return "";
            return Convert.ToString(networkGrid.SelectedRows[0].Cells["Path"].Value);
        }

        private string SelectedNetworkKey()
        {
            if (networkGrid.SelectedRows.Count == 0) return "";
            DataGridViewRow row = networkGrid.SelectedRows[0];
            return NetworkKey(
                Convert.ToInt32(row.Cells["PID"].Value, CultureInfo.InvariantCulture),
                Convert.ToString(row.Cells["Protocol"].Value),
                Convert.ToString(row.Cells["LocalAddress"].Value),
                Convert.ToString(row.Cells["LocalPort"].Value),
                Convert.ToString(row.Cells["RemoteAddress"].Value),
                Convert.ToString(row.Cells["RemotePort"].Value));
        }

        private static string NetworkKey(int pid, string protocol, string localAddress, string localPort, string remoteAddress, string remotePort)
        {
            return string.Join("\u001F", new[]
            {
                pid.ToString(CultureInfo.InvariantCulture),
                protocol ?? "",
                localAddress ?? "",
                localPort ?? "",
                remoteAddress ?? "",
                remotePort ?? ""
            });
        }

        private static int FirstDisplayedRow(DataGridView grid)
        {
            try { return grid.FirstDisplayedScrollingRowIndex; }
            catch (InvalidOperationException) { return -1; }
        }

        private static void RestoreGridPosition(DataGridView grid, int selectedIndex, int firstDisplayedRow)
        {
            if (selectedIndex >= 0 && selectedIndex < grid.Rows.Count)
            {
                grid.ClearSelection();
                grid.Rows[selectedIndex].Selected = true;
            }

            if (firstDisplayedRow >= 0 && grid.Rows.Count > 0)
            {
                try { grid.FirstDisplayedScrollingRowIndex = Math.Min(firstDisplayedRow, grid.Rows.Count - 1); }
                catch (InvalidOperationException) { }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        private async Task ExportCsvAsync(string title, string filePrefix, string rowDescription, List<IEnumerable<string>> exportRows)
        {
            if (exportRows == null || exportRows.Count <= 1)
            {
                MessageBox.Show(this, "There are no rows to export.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                AddExtension = true,
                RestoreDirectory = true,
                FileName = filePrefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    await Task.Run(() => CsvFileWriter.Write(dialog.FileName, exportRows));
                    MessageBox.Show(this, "Exported " + (exportRows.Count - 1).ToString(CultureInfo.CurrentCulture) + " " + rowDescription + " to:\n" + dialog.FileName,
                        "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "CSV export failed.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async Task ExportProcessesAsync()
        {
            List<ProcessRow> processes = ProcessRowsForCurrentView();
            var exportRows = new List<IEnumerable<string>>
            {
                new[] { "Snapshot", "PID", "Process", "User", "CPUPercent", "CPUSampled", "PrivateBytesMB", "WorkingSetMB", "PeakWorkingSetMB", "Threads", "Path" }
            };
            foreach (ProcessRow process in processes)
            {
                exportRows.Add(ProcessExportFields(process, latestProcessSnapshot).Select(SpreadsheetSafe).ToArray());
            }
            await ExportCsvAsync("Export Processes CSV", "processes", "process rows", exportRows);
        }

        private async Task ExportNetworkAsync()
        {
            List<NetworkRow> connections = NetworkRowsForCurrentView();
            var exportRows = new List<IEnumerable<string>>
            {
                new[] { "Snapshot", "Application", "PID", "User", "Protocol", "LocalAddress", "LocalPort", "RemoteAddress", "RemotePort", "State", "Path" }
            };
            foreach (NetworkRow connection in connections)
            {
                exportRows.Add(NetworkExportFields(connection).Select(SpreadsheetSafe).ToArray());
            }
            await ExportCsvAsync("Export Network CSV", "network-connections", "network rows", exportRows);
        }

        private async Task ExportAppsAsync()
        {
            List<AppProfile> apps = AppProfilesForCurrentView();
            var exportRows = new List<IEnumerable<string>>
            {
                new[] { "Snapshot", "Application", "Firewall", "ProcessCount", "CPUPercent", "CPUSampledProcesses", "Connections", "PrivateBytesMB", "WorkingSetMB", "User", "Path" }
            };
            foreach (AppProfile app in apps)
            {
                exportRows.Add(AppExportFields(app, latestAppsSnapshot, GetFirewallStatus(app.Path)).Select(SpreadsheetSafe).ToArray());
            }
            await ExportCsvAsync("Export Apps CSV", "apps", "grouped apps", exportRows);
        }

        internal static string[] AppExportFields(AppProfile app, DateTime snapshot, string firewallStatus)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            return new[]
            {
                snapshot == DateTime.MinValue ? "" : snapshot.ToString("s", CultureInfo.InvariantCulture),
                app.Name ?? "",
                firewallStatus ?? "Unknown",
                app.Pids.Count.ToString(CultureInfo.InvariantCulture),
                app.CpuSampleCount == 0 ? "" : app.Cpu.ToString("0.0", CultureInfo.InvariantCulture),
                app.CpuSampleCount.ToString(CultureInfo.InvariantCulture),
                app.ConnectionCount.ToString(CultureInfo.InvariantCulture),
                app.PrivateMb.ToString("0.0", CultureInfo.InvariantCulture),
                app.RamMb.ToString("0.0", CultureInfo.InvariantCulture),
                app.User ?? "",
                app.Path ?? ""
            };
        }

        internal static string[] ProcessExportFields(ProcessRow process, DateTime snapshot)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            return new[]
            {
                snapshot == DateTime.MinValue ? "" : snapshot.ToString("s", CultureInfo.InvariantCulture),
                process.Pid.ToString(CultureInfo.InvariantCulture),
                process.Name ?? "",
                process.User ?? "",
                process.CpuSampleAvailable ? process.Cpu.ToString("0.0", CultureInfo.InvariantCulture) : "",
                process.CpuSampleAvailable ? "true" : "false",
                process.PrivateMb.ToString("0.0", CultureInfo.InvariantCulture),
                process.WorkingSetMb.ToString("0.0", CultureInfo.InvariantCulture),
                process.PeakWorkingSetMb.ToString("0.0", CultureInfo.InvariantCulture),
                process.Threads.ToString(CultureInfo.InvariantCulture),
                process.Path ?? ""
            };
        }

        internal static string[] NetworkExportFields(NetworkRow connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            return new[]
            {
                connection.Timestamp == DateTime.MinValue ? "" : connection.Timestamp.ToString("s", CultureInfo.InvariantCulture),
                connection.Process ?? "",
                connection.Pid.ToString(CultureInfo.InvariantCulture),
                connection.User ?? "",
                connection.Protocol ?? "",
                connection.LocalAddress ?? "",
                connection.LocalPort ?? "",
                connection.RemoteAddress ?? "",
                connection.RemotePort ?? "",
                NormalizeConnectionState(connection.State),
                connection.Path ?? ""
            };
        }

        private async Task ExportHistoryAsync()
        {
            var exportRows = new List<IEnumerable<string>>
            {
                historyList.Columns.Cast<ColumnHeader>().Select(column => SpreadsheetSafe(column.Text)).ToArray()
            };
            foreach (string[] row in visibleHistoryRows)
            {
                exportRows.Add(historyList.Columns.Cast<ColumnHeader>()
                    .Select((column, index) => SpreadsheetSafe(index < row.Length ? row[index] : ""))
                    .ToArray());
            }
            await ExportCsvAsync("Export History CSV", "connection-history", "history rows", exportRows);
        }

        internal static string SpreadsheetSafe(string value)
        {
            value = value ?? "";
            if (value.Length == 0) return value;
            char first = value[0];
            return first == '=' || first == '+' || first == '-' || first == '@' || first == '\t' || first == '\r'
                ? "'" + value
                : value;
        }

        private void SaveNetworkHistory(List<NetworkRow> rows)
        {
            try
            {
                historyStore.SaveSnapshot(rows, DateTime.Now);
            }
            catch { }
        }

        private void UpdateBandwidthLabel()
        {
            try
            {
                BandwidthSnapshot snapshot = bandwidthSampler.GetSnapshot();
                if (snapshot.SampleAvailable)
                {
                    bandwidthLabel.Text = "Total adapter bandwidth: Down " + snapshot.DownKilobytesPerSecond.ToString("0.0", CultureInfo.CurrentCulture) +
                        " KB/s, Up " + snapshot.UpKilobytesPerSecond.ToString("0.0", CultureInfo.CurrentCulture) +
                        " KB/s (" + snapshot.MatchedAdapters.ToString(CultureInfo.CurrentCulture) + " stable adapter" + (snapshot.MatchedAdapters == 1 ? "" : "s") + ")";
                }
                else
                {
                    bandwidthLabel.Text = "Total bandwidth: waiting for stable per-adapter sample";
                }
            }
            catch
            {
                bandwidthLabel.Text = "Total bandwidth: unavailable";
            }
        }

        private static string GetProcessPathFast(int pid)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return "";
                var buffer = new StringBuilder(4096);
                int size = buffer.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : "";
            }
            catch
            {
                return "";
            }
            finally
            {
                if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
            }
        }

        private static string GetProcessUserFast(int pid)
        {
            IntPtr processHandle = IntPtr.Zero;
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                processHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (processHandle == IntPtr.Zero) return "";
                if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out tokenHandle)) return "";
                using (var identity = new WindowsIdentity(tokenHandle))
                {
                    return NormalizeDisplayText(identity.Name);
                }
            }
            catch
            {
                return "";
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero) NativeMethods.CloseHandle(tokenHandle);
                if (processHandle != IntPtr.Zero) NativeMethods.CloseHandle(processHandle);
            }
        }

        private static string NativeMemoryResultText(int result)
        {
            if (result == 0) return "Success.";
            uint unsigned = unchecked((uint)result);
            if (unsigned == 0xC0000061) return "Failed: Windows says the required privilege is not available. Try running from an elevated local administrator session.";
            if (unsigned == 0xC0000005) return "Failed: Windows denied access.";
            return "Failed: Windows returned native status 0x" + unsigned.ToString("X8", CultureInfo.InvariantCulture) + ".";
        }

        private static string NormalizeDisplayText(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return value
                .Replace("Ã„", "Ä")
                .Replace("Ã–", "Ö")
                .Replace("Ãœ", "Ü")
                .Replace("Ã¤", "ä")
                .Replace("Ã¶", "ö")
                .Replace("Ã¼", "ü")
                .Replace("ÃŸ", "ß")
                .Replace("Â", "")
                .Replace("™", "Ö");
        }

        private static string NormalizeConnectionState(string state)
        {
            state = NormalizeDisplayText(state);
            if (string.IsNullOrWhiteSpace(state)) return "";

            string upper = state.ToUpperInvariant();
            if (upper.Contains("HERGESTELLT") || upper == "ESTABLISHED") return "Established";
            if (upper.Contains("ABHÖREN") || upper.Contains("ABHREN") || upper == "LISTENING") return "Listening";
            if (upper.Contains("WARTEND") || upper == "TIME_WAIT") return "Time Wait";
            if (upper.Contains("SCHLIESSEN") || upper == "CLOSE_WAIT") return "Close Wait";
            if (upper.Contains("FIN_WAIT_1")) return "Fin Wait 1";
            if (upper.Contains("FIN_WAIT_2")) return "Fin Wait 2";
            return state;
        }

        private static string RuleNameForPath(string path)
        {
            using (var sha = SHA1.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Replace("-", "").Substring(0, 12);
                return "BetterTaskManager Block " + hash;
            }
        }

        private string GetFirewallStatus(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Unknown";
            string cached;
            return firewallStatusCache.TryGetValue(path, out cached) ? cached : "Unknown";
        }

        private static string FirewallExplanation(string path, string status)
        {
            if (string.IsNullOrWhiteSpace(path)) return "No executable path is available for a program-specific rule.";
            if (status == FirewallStatusBlocked) return "Active outbound block on all profiles: " + RuleNameForPath(path);
            if (status == FirewallStatusNoBlock) return "No Better Task Manager outbound block rule. Other Windows Firewall policies may still apply.";
            return "Better Task Manager could not read the rule state. Rule name: " + RuleNameForPath(path);
        }

        private void ShowCommandFailure(string action, CommandResult result)
        {
            MessageBox.Show(this, action + " failed.\n\n" + result.FailureSummary(), "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static int SafeThreadCount(Process process)
        {
            try { return process.Threads.Count; } catch { return 0; }
        }

        private static long SafeProcessStartTimeUtcTicks(Process process)
        {
            try { return process.StartTime.ToUniversalTime().Ticks; } catch { return 0; }
        }

        private static double ToMb(long bytes)
        {
            return Math.Round(bytes / 1024d / 1024d, 1);
        }

        private static void ApplyNativeDarkTheme(Control control)
        {
            if (control == null) return;
            if (control.IsHandleCreated)
            {
                try { NativeMethods.SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { }
            }

            foreach (Control child in control.Controls)
            {
                ApplyNativeDarkTheme(child);
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Console.WriteLine(SelfTest());
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Self-test failed: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            if (args != null && args.Any(a =>
                string.Equals(a, "--ui-smoke-test", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--history-ui-smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                RunUiSmokeTest();
                return;
            }

            Run();
        }

        private static void RunUiSmokeTest()
        {
            ConfigureApplicationVisuals();

            int completed = 0;
            string temporaryFolder = Path.Combine(Path.GetTempPath(), "BetterTaskManager-HistoryUiTest-" + Guid.NewGuid().ToString("N"));
            var form = new MainForm(true,
                Path.Combine(temporaryFolder, "network-history.csv"),
                Path.Combine(temporaryFolder, "settings.json"));
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                if (System.Threading.Interlocked.CompareExchange(ref completed, 1, 0) == 0)
                {
                    Environment.Exit(2);
                }
            });

            form.Shown += async (s, e) =>
            {
                try
                {
                    await form.RunUiSmokeTestAsync();
                    await Task.Delay(500);
                    System.Threading.Interlocked.Exchange(ref completed, 1);
                    Environment.ExitCode = 0;
                    form.Close();
                }
                catch (Exception ex)
                {
                    WriteCrashLog(ex);
                    System.Threading.Interlocked.Exchange(ref completed, 1);
                    Environment.ExitCode = 1;
                    form.Close();
                }
            };

            try
            {
                Application.Run(form);
            }
            finally
            {
                try { if (Directory.Exists(temporaryFolder)) Directory.Delete(temporaryFolder, true); } catch { }
            }
        }

        public static void Run()
        {
            ConfigureApplicationVisuals();
            Application.ThreadException += (s, e) =>
            {
                WriteCrashLog(e.Exception);
                MessageBox.Show(e.Exception.Message + "\n\nA crash log was written to %LOCALAPPDATA%\\BetterTaskManager\\crash.log", "Better Task Manager Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null) WriteCrashLog(ex);
                MessageBox.Show(ex == null ? "Unknown error" : ex.Message + "\n\nA crash log was written to %LOCALAPPDATA%\\BetterTaskManager\\crash.log", "Better Task Manager Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Application.Run(new MainForm());
        }

        private static void ConfigureApplicationVisuals()
        {
            ApplicationConfiguration.Initialize();
            TryEnableNativeDarkControls();
#pragma warning disable WFO5001
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        }

        private static void TryEnableNativeDarkControls()
        {
            try
            {
                NativeMethods.SetPreferredAppMode(2);
                NativeMethods.FlushMenuThemes();
            }
            catch { }
        }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), DateTime.Now.ToString("s") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        public static string SelfTest()
        {
            CommandResult success = CommandRunner.Run("cmd.exe", "/d", "/c", "echo better-task-manager-self-test");
            if (!success.Succeeded) throw new InvalidOperationException("Command runner success probe failed. " + success.FailureSummary());
            if (success.StandardOutput.IndexOf("better-task-manager-self-test", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Command runner did not capture standard output.");

            CommandResult failure = CommandRunner.Run("cmd.exe", "/d", "/c", "echo expected-failure 1>&2 & exit 7");
            if (failure.Succeeded || failure.ExitCode != 7) throw new InvalidOperationException("Command runner failure probe did not preserve exit code 7.");
            if (failure.StandardError.IndexOf("expected-failure", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Command runner did not capture standard error.");

            List<NativeConnection> connections = NativeNetworkCollector.GetAll();
            if (connections.Any(c =>
                (c.Protocol != "TCP" && c.Protocol != "UDP") ||
                !System.Net.IPAddress.TryParse(c.LocalAddress, out _) ||
                (c.Protocol == "TCP" && !System.Net.IPAddress.TryParse(c.RemoteAddress, out _)) ||
                (c.Protocol == "UDP" && (!string.IsNullOrEmpty(c.RemoteAddress) || c.RemotePort != 0)) ||
                string.IsNullOrWhiteSpace(c.State) ||
                c.LocalPort < 0 || c.LocalPort > 65535 ||
                c.RemotePort < 0 || c.RemotePort > 65535 ||
                c.OwningPid < 0))
            {
                throw new InvalidOperationException("Native network collector returned an invalid row.");
            }

            var previousAdapters = new Dictionary<string, AdapterCounters>(StringComparer.OrdinalIgnoreCase)
            {
                ["stable"] = new AdapterCounters { Received = 1000, Sent = 2000 },
                ["reset"] = new AdapterCounters { Received = 500, Sent = 500 },
                ["removed"] = new AdapterCounters { Received = 900, Sent = 900 }
            };
            var currentAdapters = new Dictionary<string, AdapterCounters>(StringComparer.OrdinalIgnoreCase)
            {
                ["stable"] = new AdapterCounters { Received = 2024, Sent = 3024 },
                ["reset"] = new AdapterCounters { Received = 100, Sent = 100 },
                ["new"] = new AdapterCounters { Received = 5000, Sent = 5000 }
            };
            double downRate;
            double upRate;
            int matchedAdapters;
            bool bandwidthCalculated = NetworkBandwidthSampler.TryCalculateRates(previousAdapters, currentAdapters, 2, out downRate, out upRate, out matchedAdapters);
            bool zeroElapsedCalculated = NetworkBandwidthSampler.TryCalculateRates(previousAdapters, currentAdapters, 0, out _, out _, out _);
            if (!bandwidthCalculated || Math.Abs(downRate - 0.5) > 0.001 || Math.Abs(upRate - 0.5) > 0.001 || matchedAdapters != 1 || zeroElapsedCalculated)
            {
                throw new InvalidOperationException("Per-adapter bandwidth delta calculation failed.");
            }

            SystemMemorySnapshot memory = NativeMemoryCollector.GetSnapshot();
            if (memory.PhysicalTotalBytes == 0 || memory.PhysicalAvailableBytes > memory.PhysicalTotalBytes ||
                memory.CommitTotalBytes > memory.CommitLimitBytes || memory.PhysicalLoadPercent < 0 || memory.PhysicalLoadPercent > 100 ||
                memory.ProcessCount == 0 || memory.ThreadCount == 0)
            {
                throw new InvalidOperationException("Native memory collector returned an invalid system snapshot.");
            }

            double systemCpuUsage;
            bool cpuCalculated = NativeCpuCollector.TryCalculateUsage(
                new SystemCpuTimes { Idle = 100, Kernel = 200, User = 100 },
                new SystemCpuTimes { Idle = 150, Kernel = 300, User = 200 },
                out systemCpuUsage);
            double ignoredCpuUsage;
            bool invalidCpuCalculated = NativeCpuCollector.TryCalculateUsage(
                new SystemCpuTimes { Idle = 100, Kernel = 200, User = 100 },
                new SystemCpuTimes { Idle = 90, Kernel = 250, User = 150 },
                out ignoredCpuUsage);
            if (!cpuCalculated || Math.Abs(systemCpuUsage - 75) > 0.001 || invalidCpuCalculated)
            {
                throw new InvalidOperationException("Native system CPU delta calculation failed.");
            }

            TestHistoryStore();
            TestSettingsStore();
            TestAppAggregation();
            if (!MainForm.HistoryRowMatchesFilter(new[] { "2026-01-01", "browser", "42", "user", "TCP", "127.0.0.1", "5000", "10.0.0.1", "443", "Established", "C:\\browser.exe" }, "443") ||
                MainForm.HistoryRowMatchesFilter(new[] { "browser", "Established" }, "missing") ||
                !MainForm.HistoryRowMatchesFilter(new[] { "browser" }, ""))
            {
                throw new InvalidOperationException("History filtering failed.");
            }
            var mixedHistoryPorts = new List<string[]>
            {
                new[] { "2026-01-03T00:00:00", "https", "3", "user", "TCP", "127.0.0.1", "5002", "10.0.0.3", "443", "Established", "C:\\https.exe" },
                new[] { "2026-01-01T00:00:00", "udp", "1", "user", "UDP", "0.0.0.0", "5353", "*", "", "Listening", "C:\\udp.exe" },
                new[] { "2026-01-02T00:00:00", "dns", "2", "user", "TCP", "127.0.0.1", "5001", "10.0.0.2", "53", "Established", "C:\\dns.exe" }
            };
            List<string[]> portsAscending = MainForm.SortHistoryRowsForView(mixedHistoryPorts, 8, true);
            List<string[]> portsDescending = MainForm.SortHistoryRowsForView(mixedHistoryPorts, 8, false);
            List<string[]> timestampsAscending = MainForm.SortHistoryRowsForView(mixedHistoryPorts, 0, true);
            if (portsAscending[0][1] != "udp" || portsAscending[1][1] != "dns" || portsAscending[2][1] != "https" ||
                portsDescending[0][1] != "https" || timestampsAscending[0][1] != "udp" || timestampsAscending[2][1] != "https")
            {
                throw new InvalidOperationException("Typed History sorting failed for mixed TCP/UDP ports or timestamps.");
            }
            var filterProbe = new ProcessRow { Pid = 4242, Name = "browser", User = "TEST\\User", Path = "C:\\Apps\\browser.exe" };
            if (!MainForm.ProcessRowMatchesFilter(filterProbe, "4242") ||
                !MainForm.ProcessRowMatchesFilter(filterProbe, "test\\user") ||
                !MainForm.ProcessRowMatchesFilter(filterProbe, "browser.exe") ||
                MainForm.ProcessRowMatchesFilter(filterProbe, "missing"))
            {
                throw new InvalidOperationException("Process snapshot filtering failed.");
            }
            var networkFilterProbe = new NetworkRow
            {
                Pid = 4242,
                Process = "browser",
                User = "TEST\\User",
                Protocol = "TCP",
                LocalAddress = "127.0.0.1",
                LocalPort = "5000",
                RemoteAddress = "10.0.0.1",
                RemotePort = "443",
                State = "Established",
                Path = "C:\\Apps\\browser.exe"
            };
            if (!MainForm.NetworkRowMatchesFilter(networkFilterProbe, "443") ||
                !MainForm.NetworkRowMatchesFilter(networkFilterProbe, "established") ||
                !MainForm.NetworkRowMatchesFilter(networkFilterProbe, "browser.exe") ||
                MainForm.NetworkRowMatchesFilter(networkFilterProbe, "missing"))
            {
                throw new InvalidOperationException("Network snapshot filtering failed.");
            }
            filterProbe.Cpu = 12.3;
            filterProbe.CpuSampleAvailable = true;
            filterProbe.PrivateMb = 100.5;
            filterProbe.WorkingSetMb = 80.2;
            filterProbe.PeakWorkingSetMb = 120.7;
            filterProbe.Threads = 9;
            string[] processExportFields = MainForm.ProcessExportFields(filterProbe, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local));
            if (processExportFields.Length != 11 || processExportFields[0] != "2026-01-02T03:04:05" || processExportFields[1] != "4242" ||
                processExportFields[4] != "12.3" || processExportFields[5] != "true" || processExportFields[6] != "100.5" ||
                processExportFields[7] != "80.2" || processExportFields[8] != "120.7" || processExportFields[9] != "9")
            {
                throw new InvalidOperationException("Process CSV fields do not match the typed snapshot model.");
            }
            filterProbe.CpuSampleAvailable = false;
            if (MainForm.ProcessExportFields(filterProbe, DateTime.MinValue)[4] != "" || MainForm.ProcessExportFields(filterProbe, DateTime.MinValue)[5] != "false")
            {
                throw new InvalidOperationException("Process CSV did not preserve unavailable CPU state.");
            }

            networkFilterProbe.Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
            string[] networkExportFields = MainForm.NetworkExportFields(networkFilterProbe);
            if (networkExportFields.Length != 11 || networkExportFields[0] != "2026-01-02T03:04:05" || networkExportFields[2] != "4242" ||
                networkExportFields[6] != "5000" || networkExportFields[8] != "443" || networkExportFields[9] != "Established" ||
                networkExportFields[10] != "C:\\Apps\\browser.exe")
            {
                throw new InvalidOperationException("Network CSV fields do not match the typed snapshot model.");
            }
            var appFilterProbe = new AppProfile { Name = "browser", Path = "C:\\Apps\\browser.exe", User = "TEST\\User", ConnectionCount = 7, Cpu = 7.5, CpuSampleCount = 1 };
            appFilterProbe.Pids.Add(4242);
            if (!MainForm.AppProfileMatchesFilter(appFilterProbe, "4242", "BTM Blocked") ||
                !MainForm.AppProfileMatchesFilter(appFilterProbe, "blocked", "BTM Blocked") ||
                !MainForm.AppProfileMatchesFilter(appFilterProbe, "browser.exe", "BTM Blocked") ||
                !MainForm.AppProfileMatchesFilter(appFilterProbe, appFilterProbe.Cpu.ToString("0.0", CultureInfo.CurrentCulture), "BTM Blocked") ||
                MainForm.AppProfileMatchesFilter(appFilterProbe, "missing", "BTM Blocked"))
            {
                throw new InvalidOperationException("Apps snapshot filtering failed.");
            }
            if (MainForm.AppCpuDisplayText(new AppProfile { Cpu = 0, CpuSampleCount = 0 }) != "..." ||
                MainForm.AppCpuDisplayText(new AppProfile { Cpu = 0, CpuSampleCount = 1 }) != 0d.ToString("0.0", CultureInfo.CurrentCulture))
            {
                throw new InvalidOperationException("Unavailable CPU sampling was not distinguished from measured idle CPU.");
            }
            int pathResolutionCalls = 0;
            int userResolutionCalls = 0;
            ProcessDetails resolvedDetails = MainForm.ResolveMissingProcessDetails(null,
                () => { pathResolutionCalls++; return "C:\\Apps\\cached.exe"; },
                () => { userResolutionCalls++; return "TEST\\Cached"; });
            ProcessDetails reusedDetails = MainForm.ResolveMissingProcessDetails(resolvedDetails,
                () => { pathResolutionCalls++; return "unexpected"; },
                () => { userResolutionCalls++; return "unexpected"; });
            if (pathResolutionCalls != 1 || userResolutionCalls != 1 || reusedDetails.Path != resolvedDetails.Path || reusedDetails.User != resolvedDetails.User)
            {
                throw new InvalidOperationException("Resolved process identity data was not reused.");
            }
            ProcessDetails deniedDetails = MainForm.ResolveMissingProcessDetails(null,
                () => { pathResolutionCalls++; return ""; },
                () => { userResolutionCalls++; return ""; });
            MainForm.ResolveMissingProcessDetails(deniedDetails,
                () => { pathResolutionCalls++; return "unexpected"; },
                () => { userResolutionCalls++; return "unexpected"; });
            if (pathResolutionCalls != 2 || userResolutionCalls != 2 || !deniedDetails.PathResolved || !deniedDetails.UserResolved)
            {
                throw new InvalidOperationException("Failed process identity lookups were not cached.");
            }
            resolvedDetails.ProcessStartTimeUtcTicks = 100;
            if (!MainForm.CachedDetailsMatchProcessInstance(resolvedDetails, 100) || MainForm.CachedDetailsMatchProcessInstance(resolvedDetails, 200))
            {
                throw new InvalidOperationException("Process identity cache did not reject PID reuse.");
            }
            if (MainForm.RefreshIntervalMilliseconds(0) != 1000 || MainForm.RefreshIntervalMilliseconds(1) != 2000 ||
                MainForm.RefreshIntervalMilliseconds(2) != 5000 || MainForm.RefreshIntervalMilliseconds(3) != 15000)
            {
                throw new InvalidOperationException("Live monitoring interval mapping failed.");
            }
            if (MainForm.ClampWindowDimension(2000, 800, 1600, 1200) != 1600 ||
                MainForm.ClampWindowDimension(400, 800, 1600, 1200) != 800 ||
                MainForm.ClampWindowDimension(0, 800, 1600, 1200) != 1200)
            {
                throw new InvalidOperationException("Window dimension clamping failed.");
            }
            if (MainForm.ClampColumnWidth(10) != 40 || MainForm.ClampColumnWidth(5000) != 1200 ||
                !MainForm.ShouldPersistMaximized(FormWindowState.Minimized, FormWindowState.Maximized) ||
                MainForm.ShouldPersistMaximized(FormWindowState.Minimized, FormWindowState.Normal))
            {
                throw new InvalidOperationException("Column-width or maximized-state persistence policy failed.");
            }
            if (!string.Equals(MainForm.ExecutableDirectory("C:\\Apps\\Tool\\tool.exe"), "C:\\Apps\\Tool", StringComparison.OrdinalIgnoreCase) ||
                MainForm.ExecutableDirectory("") != "")
            {
                throw new InvalidOperationException("Executable folder resolution failed.");
            }
            if (MainForm.ShouldShowRefreshDialog(true) || !MainForm.ShouldShowRefreshDialog(false))
            {
                throw new InvalidOperationException("Automatic refresh dialog suppression policy failed.");
            }
            if (MainForm.GetGlobalShortcutCommand(Keys.F5, "Memory") != GlobalShortcutCommand.Refresh ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.D1, "Memory") != GlobalShortcutCommand.OpenApps ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.NumPad5, "Apps") != GlobalShortcutCommand.OpenMemory ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.L, "Memory") != GlobalShortcutCommand.ToggleLive ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.F, "Network") != GlobalShortcutCommand.FocusFilter ||
                MainForm.GetGlobalShortcutCommand(Keys.Escape, "Apps") != GlobalShortcutCommand.ClearFilter ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.E, "History") != GlobalShortcutCommand.Export ||
                MainForm.GetGlobalShortcutCommand(Keys.Control | Keys.E, "Memory") != GlobalShortcutCommand.None ||
                MainForm.GetGlobalShortcutCommand(Keys.PageUp, "History") != GlobalShortcutCommand.PreviousPage ||
                MainForm.GetGlobalShortcutCommand(Keys.PageDown, "Network") != GlobalShortcutCommand.None)
            {
                throw new InvalidOperationException("Global keyboard shortcut mapping failed.");
            }

            using (var form = new MainForm())
            {
                if (Application.ProductVersion != "1.1.0-preview.30" || form.Text != "Better Task Manager v1.1.0-preview.30")
                {
                    throw new InvalidOperationException("Application version metadata and window title do not match 1.1.0-preview.30.");
                }
                return "Self-test OK for v" + Application.ProductVersion + ". UI construction, command handling, bounded history, native memory, and " + connections.Count + " native network rows passed.";
            }
        }

        private static void TestHistoryStore()
        {
            string temporaryFolder = Path.Combine(Path.GetTempPath(), "BetterTaskManager-SelfTest-" + Guid.NewGuid().ToString("N"));
            string historyPath = Path.Combine(temporaryFolder, "network-history.csv");
            try
            {
                var store = new NetworkHistoryStore(historyPath);
                DateTime firstSeen = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
                var row = new NetworkRow
                {
                    Timestamp = firstSeen,
                    Process = "Test, App",
                    Pid = 4242,
                    User = "TEST\\User",
                    Protocol = "TCP",
                    LocalAddress = "127.0.0.1",
                    LocalPort = "12345",
                    RemoteAddress = "127.0.0.1",
                    RemotePort = "443",
                    State = "Established",
                    Path = "C:\\Apps\\Test, \"Quoted\"\\app.exe"
                };

                if (store.SaveSnapshot(new[] { row }, firstSeen) != 1) throw new InvalidOperationException("History store did not save the first connection observation.");
                if (store.SaveSnapshot(new[] { row }, firstSeen.AddSeconds(2)) != 0) throw new InvalidOperationException("History store duplicated an unchanged connection.");
                if (store.SaveSnapshot(Array.Empty<NetworkRow>(), firstSeen.AddSeconds(4)) != 0) throw new InvalidOperationException("History store wrote an empty snapshot.");

                row.Timestamp = firstSeen.AddSeconds(6);
                if (store.SaveSnapshot(new[] { row }, row.Timestamp) != 1) throw new InvalidOperationException("History store did not record a connection that reappeared.");

                List<string[]> loaded = store.LoadRecent(100);
                if (loaded.Count != 2 || loaded[0][10] != row.Path || loaded[0][1] != row.Process) throw new InvalidOperationException("History CSV round-trip failed.");
                if (store.LoadRecent(1).Count != 1) throw new InvalidOperationException("History row limit was not enforced.");

                string exportPath = Path.Combine(temporaryFolder, "export.csv");
                CsvFileWriter.Write(exportPath, new[]
                {
                    new[] { "Name", "Path" },
                    new[] { "Quoted, App", "C:\\Apps\\\"Quoted\"\\app.exe" }
                });
                string[] exportLines = File.ReadAllLines(exportPath, Encoding.UTF8);
                List<string> exportedFields = exportLines.Length == 2 ? NetworkHistoryStore.ParseCsvLine(exportLines[1]) : new List<string>();
                if (exportedFields.Count != 2 || exportedFields[0] != "Quoted, App" || exportedFields[1] != "C:\\Apps\\\"Quoted\"\\app.exe")
                {
                    throw new InvalidOperationException("CSV export escaping failed.");
                }
                if (MainForm.SpreadsheetSafe("=SUM(A1:A2)") != "'=SUM(A1:A2)" || MainForm.SpreadsheetSafe("normal") != "normal")
                {
                    throw new InvalidOperationException("Spreadsheet formula protection failed.");
                }

                store.SaveSnapshot(Array.Empty<NetworkRow>(), firstSeen.AddDays(31));
                if (store.LoadRecent(100).Count != 0) throw new InvalidOperationException("History retention did not prune entries older than 30 days.");

                store.Clear();
                if (store.LoadRecent(100).Count != 0) throw new InvalidOperationException("History clear did not remove saved observations.");
                row.Timestamp = firstSeen.AddDays(31).AddSeconds(2);
                if (store.SaveSnapshot(new[] { row }, row.Timestamp) != 1 || store.LoadRecent(100).Count != 1)
                {
                    throw new InvalidOperationException("History clear did not reset connection deduplication state.");
                }
            }
            finally
            {
                try { if (Directory.Exists(temporaryFolder)) Directory.Delete(temporaryFolder, true); } catch { }
            }
        }

        private static void TestSettingsStore()
        {
            string temporaryFolder = Path.Combine(Path.GetTempPath(), "BetterTaskManager-SettingsTest-" + Guid.NewGuid().ToString("N"));
            string settingsPath = Path.Combine(temporaryFolder, "settings.json");
            try
            {
                var store = new AppSettingsStore(settingsPath);
                store.Save(new AppSettings
                {
                    WindowWidth = 1280,
                    WindowHeight = 720,
                    Maximized = true,
                    RefreshIntervalIndex = 3,
                    ColumnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Network.Process"] = 222 }
                });
                AppSettings loaded = store.Load();
                int networkWidth;
                if (loaded.WindowWidth != 1280 || loaded.WindowHeight != 720 || !loaded.Maximized || loaded.RefreshIntervalIndex != 3 ||
                    loaded.ColumnWidths == null || !loaded.ColumnWidths.TryGetValue("Network.Process", out networkWidth) || networkWidth != 222)
                {
                    throw new InvalidOperationException("App settings round-trip failed.");
                }

                File.WriteAllText(settingsPath, "{not valid json", Encoding.UTF8);
                AppSettings fallback = store.Load();
                if (fallback.WindowWidth != 1560 || fallback.WindowHeight != 900 || fallback.Maximized || fallback.RefreshIntervalIndex != 2 || fallback.ColumnWidths == null)
                {
                    throw new InvalidOperationException("Corrupt settings did not fall back to defaults.");
                }
            }
            finally
            {
                try { if (Directory.Exists(temporaryFolder)) Directory.Delete(temporaryFolder, true); } catch { }
            }
        }

        private static void TestAppAggregation()
        {
            const string sharedPath = "C:\\Apps\\Browser\\browser.exe";
            var processes = new List<ProcessRow>
            {
                new ProcessRow { Pid = 101, Name = "browser", Path = sharedPath, Cpu = 1.5, CpuSampleAvailable = true, PrivateMb = 100.5, WorkingSetMb = 80.25 },
                new ProcessRow { Pid = 202, Name = "browser", Path = sharedPath, Cpu = 2.25, CpuSampleAvailable = true, PrivateMb = 200.25, WorkingSetMb = 120.5 }
            };
            var connections = new List<NetworkRow>
            {
                new NetworkRow { Pid = 101, Process = "browser", Path = sharedPath, Protocol = "TCP" },
                new NetworkRow { Pid = 202, Process = "browser", Path = sharedPath, Protocol = "UDP" }
            };

            AppProfile profile = MainForm.BuildAppProfiles(processes, connections).Single();
            if (profile.Pids.Count != 2 || profile.ConnectionCount != 2 || profile.CpuSampleCount != 2 ||
                Math.Abs(profile.Cpu - 3.75) > 0.001 || Math.Abs(profile.PrivateMb - 300.75) > 0.001 || Math.Abs(profile.RamMb - 200.75) > 0.001)
            {
                throw new InvalidOperationException("Grouped app aggregation does not match the sum of its per-process rows.");
            }

            DateTime snapshot = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
            string[] exportFields = MainForm.AppExportFields(profile, snapshot, "No BTM Block");
            if (exportFields.Length != 11 || exportFields[0] != "2026-01-02T03:04:05" || exportFields[3] != "2" ||
                exportFields[4] != "3.8" || exportFields[5] != "2" || exportFields[6] != "2" ||
                exportFields[7] != "300.8" || exportFields[8] != "200.8" || exportFields[10] != sharedPath)
            {
                throw new InvalidOperationException("Grouped Apps CSV fields do not match the reconciled snapshot values.");
            }
            if (MainForm.AppExportFields(new AppProfile(), DateTime.MinValue, "Unknown")[4] != "")
            {
                throw new InvalidOperationException("Apps CSV did not leave unavailable CPU blank.");
            }
        }
    }
}
