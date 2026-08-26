using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
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
    }

    public sealed class ProcessRow
    {
        public int Pid;
        public string Name = "";
        public string User = "";
        public double Cpu;
        public double PrivateMb;
        public double WorkingSetMb;
        public double PeakWorkingSetMb;
        public int Threads;
        public string Path = "";
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

    public sealed class MainForm : Form
    {
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
        private readonly CheckBox liveMonitoringCheck;
        private readonly ComboBox refreshIntervalBox;
        private readonly Label liveStatusLabel;
        private readonly Button restartAdminButton;
        private readonly Label statusLabel;
        private readonly Label processSummaryLabel;
        private readonly TextBox filterBox;
        private readonly Button networkRefreshButton;
        private readonly Button blockButton;
        private readonly Button unblockButton;
        private readonly Label networkStatusLabel;
        private readonly Label bandwidthLabel;
        private readonly DataGridView historyGrid;
        private readonly Panel historyTab;
        private readonly Label historyNoteLabel;
        private readonly Button reloadHistoryButton;
        private readonly Button trimAllButton;
        private readonly Button clearStandbyButton;
        private readonly Button emptySystemButton;
        private readonly Button memoryRefreshButton;
        private readonly Label memorySnapshotLabel;
        private readonly Label memoryLoadCard;
        private readonly Label memoryUsedCard;
        private readonly Label memoryAvailableCard;
        private readonly Label memoryCommitCard;
        private readonly Label memoryCacheCard;
        private readonly Label memoryStatusLabel;
        private readonly Panel pageHost;
        private readonly FlowLayoutPanel navBar;
        private readonly Panel appsTab;
        private readonly Panel processTab;
        private readonly Panel networkTab;
        private readonly Panel memoryTab;
        private Control activePage;
        private readonly Timer timer;
        private readonly Dictionary<DataGridView, Tuple<string, bool>> gridSortState = new Dictionary<DataGridView, Tuple<string, bool>>();

        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> lastCpu = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private List<ProcessRow> latestProcessRows = new List<ProcessRow>();
        private List<NetworkRow> latestNetworkRows = new List<NetworkRow>();
        private List<AppProfile> latestAppProfiles = new List<AppProfile>();
        private DateTime latestAppsSnapshot = DateTime.MinValue;
        private DateTime latestProcessSnapshot = DateTime.MinValue;
        private DateTime latestNetworkSnapshot = DateTime.MinValue;
        private Dictionary<int, ProcessDetails> detailsCache = new Dictionary<int, ProcessDetails>();
        private bool detailsLoaded = false;
        private bool refreshingApps = false;
        private bool refreshingProcesses = false;
        private bool refreshingNetwork = false;
        private bool updatingAppGrid = false;
        private bool settingProcessFilter = false;
        private bool loadingHistory = false;
        private readonly Dictionary<string, string> firewallStatusCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly NetworkHistoryStore historyStore;
        private long lastAdapterReceived = -1;
        private long lastAdapterSent = -1;
        private DateTime lastAdapterSample = DateTime.MinValue;

        public MainForm()
        {
            Text = "Better Task Manager v" + Application.ProductVersion;
            Size = new Size(1560, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;

            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            string historyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
            historyStore = new NetworkHistoryStore(Path.Combine(historyFolder, "network-history.csv"));

            var rootShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            rootShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(rootShell);

            navBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0), Cursor = Cursors.Hand };
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
            refreshIntervalBox.SelectedIndex = 2;
            liveStatusLabel = new Label
            {
                Text = "Paused",
                AutoSize = true,
                ForeColor = Theme.MutedText,
                Margin = new Padding(4, 11, 0, 0)
            };
            navBar.Controls.AddRange(new Control[] { liveMonitoringCheck, refreshIntervalBox, liveStatusLabel });
            appsNavButton.Click += async (s, e) => { ShowPage(appsTab); await RefreshAppsAsync(false); };
            processesNavButton.Click += async (s, e) => { ShowPage(processTab); await RefreshProcessesAsync(); };
            networkNavButton.Click += async (s, e) => { ShowPage(networkTab); await RefreshNetworkAsync(); };
            historyNavButton.Click += async (s, e) => await ShowHistoryAsync();
            memoryNavButton.Click += (s, e) => { ShowPage(memoryTab); RefreshMemoryPage(); };

            var appShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
            appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 540));
            appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
                Tuple.Create("Ram", "Working Set MB"),
                Tuple.Create("Path", "Path")
            });
            appGrid.Columns["App"].Width = 170;
            appGrid.Columns["Firewall"].Width = 120;
            appGrid.Columns["Processes"].Width = 55;
            appGrid.Columns["Connections"].Width = 55;
            appGrid.Columns["Ram"].Width = 95;
            appGrid.Columns["Path"].Visible = false;
            LockGridColumns(appGrid);
            appLeft.Controls.Add(appHeader, 0, 0);
            appLeft.Controls.Add(appSearchBox, 0, 1);
            appLeft.Controls.Add(appGrid, 0, 2);

            var appRight = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(24, 18, 24, 18), Margin = new Padding(0) };
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
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

            var cardRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            appConnectionCard = MakeMetricCard("0", "Group Connections");
            appMemoryCard = MakeMetricCard("0 MB", "Sum Private Bytes");
            appRamCard = MakeMetricCard("0 MB", "Sum Working Set");
            appFirewallCard = MakeMetricCard("Unknown", "Firewall");
            cardRow.Controls.AddRange(new Control[] { appConnectionCard, appMemoryCard, appRamCard, appFirewallCard });
            appRight.Controls.Add(cardRow, 0, 1);

            var appActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            appRefreshButton = MakeButton("Refresh Apps", 120);
            appBlockButton = MakeButton("Block App", 105);
            appUnblockButton = MakeButton("Unblock App", 115);
            appViewProcessesButton = MakeButton("View Processes", 125);
            appFirewallDetailsLabel = new Label
            {
                Text = "Select an app to inspect its Better Task Manager firewall rule.",
                Width = 460,
                Height = 30,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.MutedText,
                Margin = new Padding(10, 0, 0, 0)
            };
            appActions.Controls.AddRange(new Control[] { appRefreshButton, appBlockButton, appUnblockButton, appViewProcessesButton, appFirewallDetailsLabel });
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
            processPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processTab.Controls.Add(processPanel);

            var processToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            refreshButton = MakeButton("Refresh", 90);
            killButton = MakeButton("Force Kill", 100);
            trimSelectedButton = MakeButton("Trim Selected Memory", 160);
            loadDetailsButton = MakeButton("Load Users/Paths", 130);
            var exportProcessesButton = MakeButton("Export CSV", 100);
            restartAdminButton = MakeButton("Restart as Admin", 125);
            restartAdminButton.Visible = !isAdmin;
            var filterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(12, 9, 4, 0) };
            filterBox = new TextBox { Width = 260 };
            statusLabel = new Label
            {
                Text = isAdmin ? "Running as administrator" : "Not administrator: some actions may fail",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0),
                ForeColor = isAdmin ? Theme.Good : Theme.Danger
            };
            processToolbar.Controls.AddRange(new Control[] { refreshButton, killButton, trimSelectedButton, loadDetailsButton, exportProcessesButton, restartAdminButton, filterLabel, filterBox, statusLabel });
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
            processGrid.Columns["PrivateMB"].Width = 130;
            processGrid.Columns["WorkingSetMB"].Width = 150;
            processGrid.Columns["PeakWorkingSetMB"].Width = 120;
            processGrid.Columns["Threads"].Width = 80;
            processGrid.Columns["Path"].Width = 520;
            LockGridColumns(processGrid);
            processPanel.Controls.Add(processGrid, 0, 2);

            var networkPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            networkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            networkTab.Controls.Add(networkPanel);

            var networkToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            networkRefreshButton = MakeButton("Refresh", 90);
            blockButton = MakeButton("Block App", 100);
            unblockButton = MakeButton("Unblock App", 110);
            var exportNetworkButton = MakeButton("Export CSV", 100);
            networkStatusLabel = new Label
            {
                Text = "Live ports and destinations.",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0)
            };
            bandwidthLabel = new Label
            {
                Text = "Total bandwidth: waiting for second sample",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0),
                ForeColor = Theme.Info
            };
            networkToolbar.Controls.AddRange(new Control[] { networkRefreshButton, blockButton, unblockButton, exportNetworkButton, networkStatusLabel, bandwidthLabel });
            networkPanel.Controls.Add(networkToolbar, 0, 0);

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
            networkPanel.Controls.Add(networkGrid, 0, 1);

            var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            historyTab.Controls.Add(historyPanel);
            var historyToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            reloadHistoryButton = MakeButton("Reload History", 120);
            var exportHistoryButton = MakeButton("Export CSV", 100);
            historyNoteLabel = new Label
            {
                Text = "Shows new and changed connections from the last 30 days (newest first).",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0)
            };
            historyToolbar.Controls.AddRange(new Control[] { reloadHistoryButton, exportHistoryButton, historyNoteLabel });
            historyPanel.Controls.Add(historyToolbar, 0, 0);
            historyGrid = NewGrid();
            AddColumns(historyGrid, new[] {
                Tuple.Create("Timestamp", "Timestamp"),
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
            historyPanel.Controls.Add(historyGrid, 0, 1);

            var memoryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            memoryTab.Controls.Add(memoryPanel);
            memoryPanel.Controls.Add(new Label { Text = "Memory", Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true });
            memorySnapshotLabel = new Label { Text = "Snapshot unavailable", AutoSize = true, Width = 1400, ForeColor = Theme.MutedText };
            memoryPanel.Controls.Add(memorySnapshotLabel);

            var memoryCards = new FlowLayoutPanel { Width = 1400, Height = 84, WrapContents = false, Margin = new Padding(0, 8, 0, 8) };
            memoryLoadCard = MakeMetricCard("0%", "Physical Load");
            memoryUsedCard = MakeMetricCard("0 GiB", "Used RAM");
            memoryAvailableCard = MakeMetricCard("0 GiB", "Available RAM");
            memoryCommitCard = MakeMetricCard("0 / 0 GiB", "Commit / Limit");
            memoryCacheCard = MakeMetricCard("0 GiB", "System Cache");
            memoryCards.Controls.AddRange(new Control[] { memoryLoadCard, memoryUsedCard, memoryAvailableCard, memoryCommitCard, memoryCacheCard });
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

            appRefreshButton.Click += async (s, e) => await RefreshAppsAsync(true);
            appSearchBox.TextChanged += (s, e) => { FillAppGrid(latestAppProfiles); ShowSelectedApp(); };
            appGrid.SelectionChanged += (s, e) => ShowSelectedApp();
            appBlockButton.Click += async (s, e) => await BlockSelectedAppAsync(true);
            appUnblockButton.Click += async (s, e) => await BlockSelectedAppAsync(false);
            appViewProcessesButton.Click += (s, e) => ViewSelectedAppProcesses();

            refreshButton.Click += async (s, e) => await RefreshProcessesAsync();
            loadDetailsButton.Click += async (s, e) => await LoadDetailsAndRefreshAsync();
            filterBox.TextChanged += async (s, e) => { if (!settingProcessFilter) await RefreshProcessesAsync(); };
            killButton.Click += async (s, e) => await KillSelectedAsync();
            trimSelectedButton.Click += async (s, e) => await TrimSelectedAsync();
            exportProcessesButton.Click += async (s, e) => await ExportGridAsync(processGrid, "processes");
            restartAdminButton.Click += (s, e) => RestartAsAdmin();

            networkRefreshButton.Click += async (s, e) => await RefreshNetworkAsync();
            networkTab.Enter += async (s, e) => await RefreshNetworkAsync();
            blockButton.Click += async (s, e) => await BlockSelectedAsync(true);
            unblockButton.Click += async (s, e) => await BlockSelectedAsync(false);
            exportNetworkButton.Click += async (s, e) => await ExportGridAsync(networkGrid, "network-connections");
            reloadHistoryButton.Click += async (s, e) => await LoadHistoryGridAsync();
            exportHistoryButton.Click += async (s, e) => await ExportGridAsync(historyGrid, "connection-history");

            trimAllButton.Click += async (s, e) => await TrimAllAsync();
            clearStandbyButton.Click += (s, e) => ClearStandby();
            emptySystemButton.Click += (s, e) => EmptySystemWorkingSets();
            memoryRefreshButton.Click += (s, e) => RefreshMemoryPage();

            timer = new Timer { Interval = 5000, Enabled = false };
            timer.Tick += async (s, e) =>
            {
                if (!liveMonitoringCheck.Checked) return;
                await RefreshActivePageAsync();
            };
            liveMonitoringCheck.CheckedChanged += async (s, e) =>
            {
                timer.Enabled = liveMonitoringCheck.Checked;
                liveStatusLabel.Text = liveMonitoringCheck.Checked ? "Live" : "Paused";
                liveStatusLabel.ForeColor = liveMonitoringCheck.Checked ? Theme.Good : Theme.MutedText;
                if (liveMonitoringCheck.Checked) await RefreshActivePageAsync();
            };
            refreshIntervalBox.SelectedIndexChanged += (s, e) =>
            {
                timer.Interval = RefreshIntervalMilliseconds(refreshIntervalBox.SelectedIndex);
            };

            Shown += async (s, e) =>
            {
                ApplyDarkTheme(this);
                ApplyNativeDarkTheme(this);
                ShowPage(appsTab);
                await RefreshAppsAsync(true);
            };

            ApplyDarkTheme(this);
            ShowPage(appsTab);
        }

        private async Task RefreshActivePageAsync()
        {
            if (activePage == appsTab) await RefreshAppsAsync(false);
            else if (activePage == processTab) await RefreshProcessesAsync();
            else if (activePage == networkTab) await RefreshNetworkAsync();
            else if (activePage == memoryTab) RefreshMemoryPage();
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
                FillAppGrid(SortApps(latestAppProfiles, columnName, ascending));
                ShowSelectedApp();
            }
            else if (grid == appConnectionsGrid)
            {
                SortVisibleGrid(grid, columnName, ascending);
            }
            else if (grid == processGrid)
            {
                FillProcessGrid(SortProcesses(latestProcessRows, columnName, ascending));
            }
            else if (grid == networkGrid || grid == historyGrid)
            {
                SortVisibleGrid(grid, columnName, ascending);
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

        private static List<AppProfile> SortApps(List<AppProfile> apps, string columnName, bool ascending)
        {
            IEnumerable<AppProfile> query;
            if (columnName == "Processes") query = apps.OrderBy(a => a.Pids.Count);
            else if (columnName == "Connections") query = apps.OrderBy(a => a.ConnectionCount);
            else if (columnName == "Ram") query = apps.OrderBy(a => a.RamMb);
            else query = apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
            if (!ascending) query = query.Reverse();
            return query.ToList();
        }

        private static List<ProcessRow> SortProcesses(List<ProcessRow> rows, string columnName, bool ascending)
        {
            IEnumerable<ProcessRow> query;
            if (columnName == "PID") query = rows.OrderBy(r => r.Pid);
            else if (columnName == "CPU") query = rows.OrderBy(r => r.Cpu);
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

        private async Task RefreshAppsAsync(bool refreshFirewall)
        {
            if (refreshingApps) return;
            refreshingApps = true;
            appRefreshButton.Enabled = false;
            appTitleLabel.Text = "Loading apps...";
            appMetaLabel.Text = "Collecting processes, connections, and firewall state";
            try
            {
                string filter = "";
                var cache = detailsCache;
                var knownFirewallStatuses = new Dictionary<string, string>(firewallStatusCache, StringComparer.OrdinalIgnoreCase);
                var data = await Task.Run(() =>
                {
                    DateTime snapshotTime = DateTime.Now;
                    var processes = BuildProcessRows(filter, cache);
                    var network = BuildNetworkRows(processes);
                    var apps = BuildAppProfiles(processes, network);
                    var firewall = refreshFirewall ? LoadFirewallStatuses(apps) : knownFirewallStatuses;
                    SaveNetworkHistory(network);
                    return Tuple.Create(processes, network, apps, firewall, snapshotTime);
                });

                latestProcessRows = data.Item1;
                latestNetworkRows = data.Item2;
                latestAppProfiles = data.Item3;
                latestAppsSnapshot = data.Item5;
                latestProcessSnapshot = data.Item5;
                latestNetworkSnapshot = data.Item5;
                firewallStatusCache.Clear();
                foreach (var pair in data.Item4) firewallStatusCache[pair.Key] = pair.Value;
                FillAppGrid(latestAppProfiles);
                UpdateBandwidthLabel();
                ShowSelectedApp();
            }
            catch (Exception ex)
            {
                appTitleLabel.Text = "Refresh failed";
                appMetaLabel.Text = ex.Message;
            }
            finally
            {
                appRefreshButton.Enabled = true;
                refreshingApps = false;
            }
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

        private void FillAppGrid(List<AppProfile> apps)
        {
            string search = appSearchBox.Text.Trim().ToLowerInvariant();
            string previousPath = null;
            if (appGrid.SelectedRows.Count > 0) previousPath = Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);

            updatingAppGrid = true;
            appGrid.SuspendLayout();
            try
            {
                appGrid.Rows.Clear();
                foreach (var app in apps)
                {
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        string haystack = (app.Name + " " + app.Path + " " + app.User).ToLowerInvariant();
                        if (!haystack.Contains(search)) continue;
                    }

                    int index = appGrid.Rows.Add(app.Name, GetFirewallStatus(app.Path), app.Pids.Count, app.ConnectionCount,
                        app.RamMb.ToString("0.0", CultureInfo.CurrentCulture), app.Path);
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
                appConnectionsGrid.Rows.Clear();
                return;
            }

            AppProfile app = SelectedAppProfile();
            if (app == null) return;

            appTitleLabel.Text = app.Name;
            string pids = app.Pids.Count == 0 ? "No active PID" : "PID " + string.Join(", ", app.Pids.Take(8).Select(p => p.ToString(CultureInfo.InvariantCulture)));
            if (app.Pids.Count > 8) pids += " +" + (app.Pids.Count - 8).ToString(CultureInfo.InvariantCulture);
            appMetaLabel.Text = SnapshotLabel(latestAppsSnapshot) + "    " + app.Pids.Count.ToString(CultureInfo.CurrentCulture) + " processes aggregated    " + pids +
                "    " + (string.IsNullOrWhiteSpace(app.User) ? "User unknown" : app.User) + "    " + (string.IsNullOrWhiteSpace(app.Path) ? "Path unavailable" : app.Path);
            appConnectionCard.Text = app.ConnectionCount.ToString(CultureInfo.InvariantCulture) + "\nGroup Connections";
            appMemoryCard.Text = app.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Private Bytes";
            appRamCard.Text = app.RamMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Working Set";
            string firewallStatus = GetFirewallStatus(app.Path);
            appFirewallCard.Text = firewallStatus + "\nFirewall";
            appFirewallDetailsLabel.Text = FirewallExplanation(app.Path, firewallStatus);
            appFirewallDetailsLabel.ForeColor = firewallStatus == FirewallStatusBlocked
                ? Theme.Danger
                : firewallStatus == FirewallStatusNoBlock ? Theme.MutedText : Theme.Warning;

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

            List<ProcessRow> matchingRows = latestProcessRows.Where(row => app.Pids.Contains(row.Pid)).ToList();
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

        private async Task RefreshProcessesAsync()
        {
            if (refreshingProcesses) return;
            refreshingProcesses = true;
            refreshButton.Enabled = false;
            statusLabel.Text = "Loading processes...";
            statusLabel.ForeColor = Theme.Warning;

            try
            {
                string filter = filterBox.Text.Trim().ToLowerInvariant();
                var cache = detailsCache;
                var rows = await Task.Run(() => BuildProcessRows(filter, cache));
                latestProcessRows = rows;
                latestProcessSnapshot = DateTime.Now;
                FillProcessGrid(rows);
                statusLabel.Text = SnapshotLabel(latestProcessSnapshot) + "    " + (isAdmin
                    ? (detailsLoaded ? "Running as administrator - users/paths loaded" : "Running as administrator")
                    : "Not administrator: some actions may fail");
                statusLabel.ForeColor = isAdmin ? Theme.Good : Theme.Danger;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Process refresh failed";
                statusLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, ex.Message, "Process refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                refreshButton.Enabled = true;
                refreshingProcesses = false;
            }
        }

        private List<ProcessRow> BuildProcessRows(string filter, Dictionary<int, ProcessDetails> cache)
        {
            var now = DateTime.UtcNow;
            var result = new List<ProcessRow>();
            foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    int pid = process.Id;
                    ProcessDetails details = null;
                    cache.TryGetValue(pid, out details);
                    string title = "";
                    try { title = process.MainWindowTitle; } catch { }
                    string name = string.IsNullOrWhiteSpace(title) ? process.ProcessName : process.ProcessName + " - " + title;
                    string user = details == null ? "" : details.User;
                    string path = details == null ? "" : details.Path;

                    if (string.IsNullOrWhiteSpace(path)) path = GetProcessPathFast(pid);
                    if (string.IsNullOrWhiteSpace(user)) user = GetProcessUserFast(pid);

                    if (!string.IsNullOrEmpty(filter))
                    {
                        string haystack = (name + " " + user + " " + path).ToLowerInvariant();
                        if (!haystack.Contains(filter)) continue;
                    }

                    double cpuPercent = 0;
                    try
                    {
                        TimeSpan totalCpu = process.TotalProcessorTime;
                        Tuple<TimeSpan, DateTime> old;
                        if (lastCpu.TryGetValue(pid, out old))
                        {
                            double seconds = Math.Max(0.5, (now - old.Item2).TotalSeconds);
                            cpuPercent = Math.Max(0, Math.Round((totalCpu - old.Item1).TotalSeconds / (seconds * Environment.ProcessorCount) * 100, 1));
                        }
                        lastCpu[pid] = Tuple.Create(totalCpu, now);
                    }
                    catch { }

                    result.Add(new ProcessRow
                    {
                        Pid = pid,
                        Name = name,
                        User = user,
                        Cpu = cpuPercent,
                        PrivateMb = ToMb(process.PrivateMemorySize64),
                        WorkingSetMb = ToMb(process.WorkingSet64),
                        PeakWorkingSetMb = ToMb(process.PeakWorkingSet64),
                        Threads = SafeThreadCount(process),
                        Path = path
                    });
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
            return result;
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
                    int index = processGrid.Rows.Add(row.Pid, row.Name, NormalizeDisplayText(row.User), row.Cpu.ToString("0.0", CultureInfo.CurrentCulture),
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
            processSummaryLabel.ForeColor = Theme.Info;
            processSummaryLabel.Text = "Visible rows: " + rows.Count.ToString(CultureInfo.CurrentCulture) +
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
                detailsCache = await Task.Run(() => LoadProcessDetails());
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
                        User = GetProcessUserFast(pid)
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

        private async Task RefreshNetworkAsync()
        {
            if (refreshingNetwork) return;
            refreshingNetwork = true;
            networkRefreshButton.Enabled = false;
            networkStatusLabel.Text = "Loading network connections...";
            networkStatusLabel.ForeColor = Theme.Warning;
            try
            {
                var rows = await Task.Run(() =>
                {
                    var networkRows = BuildNetworkRows();
                    SaveNetworkHistory(networkRows);
                    return networkRows;
                });
                latestNetworkRows = rows;
                latestNetworkSnapshot = rows.Count > 0 ? rows[0].Timestamp : DateTime.Now;
                FillNetworkGrid(rows);
                UpdateBandwidthLabel();
                networkStatusLabel.Text = SnapshotLabel(latestNetworkSnapshot) + "    Loaded " + rows.Count + " network rows. Per-app bandwidth needs ETW/WFP collector.";
                networkStatusLabel.ForeColor = Theme.MutedText;
            }
            catch (Exception ex)
            {
                networkStatusLabel.Text = "Network refresh failed";
                networkStatusLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, ex.Message, "Network refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            var processNames = new Dictionary<int, string>();
            foreach (var p in Process.GetProcesses())
            {
                try { processNames[p.Id] = p.ProcessName; } catch { }
                finally { p.Dispose(); }
            }

            var snapshotDetails = new Dictionary<int, ProcessDetails>();
            foreach (var pair in detailsCache) snapshotDetails[pair.Key] = pair.Value;
            if (knownProcessRows != null)
            {
                foreach (ProcessRow processRow in knownProcessRows)
                {
                    snapshotDetails[processRow.Pid] = new ProcessDetails { Path = processRow.Path, User = processRow.User };
                }
            }

            var rows = new List<NetworkRow>();
            foreach (var connection in NativeNetworkCollector.GetAll())
            {
                ProcessDetails details = null;
                if (!snapshotDetails.TryGetValue(connection.OwningPid, out details))
                {
                    details = new ProcessDetails();
                    snapshotDetails[connection.OwningPid] = details;
                }
                string name;
                processNames.TryGetValue(connection.OwningPid, out name);
                string path = details.Path;
                string user = details.User;
                if (string.IsNullOrWhiteSpace(path)) path = GetProcessPathFast(connection.OwningPid);
                if (string.IsNullOrWhiteSpace(user)) user = GetProcessUserFast(connection.OwningPid);
                details.Path = path;
                details.User = user;

                rows.Add(new NetworkRow
                {
                    Timestamp = now,
                    Process = name ?? "",
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

        private void RefreshMemoryPage()
        {
            try
            {
                SystemMemorySnapshot snapshot = NativeMemoryCollector.GetSnapshot();
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
            }
            catch (Exception ex)
            {
                memorySnapshotLabel.ForeColor = Theme.Danger;
                memorySnapshotLabel.Text = "Memory snapshot failed: " + ex.Message;
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
            await LoadHistoryGridAsync();
        }

        private async Task LoadHistoryGridAsync()
        {
            if (loadingHistory) return;
            loadingHistory = true;
            reloadHistoryButton.Enabled = false;
            historyNoteLabel.Text = "Loading recent connection changes...";
            try
            {
                List<string[]> rows = await Task.Run(() => historyStore.LoadRecent(2000));
                var gridRows = new List<DataGridViewRow>(rows.Count);
                foreach (string[] fields in rows)
                {
                    var gridRow = new DataGridViewRow();
                    gridRow.CreateCells(historyGrid, fields.Cast<object>().ToArray());
                    gridRows.Add(gridRow);
                }

                historyGrid.SuspendLayout();
                historyGrid.Rows.Clear();
                if (gridRows.Count > 0) historyGrid.Rows.AddRange(gridRows.ToArray());
                historyNoteLabel.ForeColor = Theme.MutedText;
                historyNoteLabel.Text = rows.Count == 2000
                    ? "Showing the newest 2,000 connection changes from the last 30 days."
                    : "Showing " + rows.Count.ToString(CultureInfo.CurrentCulture) + " connection changes from the last 30 days (newest first).";
            }
            catch (Exception ex)
            {
                historyNoteLabel.Text = "History load failed: " + ex.Message;
                historyNoteLabel.ForeColor = Theme.Danger;
            }
            finally
            {
                historyGrid.ResumeLayout();
                reloadHistoryButton.Enabled = true;
                loadingHistory = false;
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
            }
            catch (Exception ex)
            {
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

        private async Task ExportGridAsync(DataGridView grid, string filePrefix)
        {
            if (grid.Rows.Cast<DataGridViewRow>().All(row => row.IsNewRow))
            {
                MessageBox.Show(this, "There are no rows to export.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Title = "Export CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                AddExtension = true,
                RestoreDirectory = true,
                FileName = filePrefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var columns = grid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Visible)
                    .OrderBy(column => column.DisplayIndex)
                    .ToList();
                var exportRows = new List<IEnumerable<string>>
                {
                    columns.Select(column => SpreadsheetSafe(column.HeaderText)).ToArray()
                };

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    exportRows.Add(columns.Select(column => SpreadsheetSafe(Convert.ToString(row.Cells[column.Index].Value, CultureInfo.CurrentCulture))).ToArray());
                }

                try
                {
                    await Task.Run(() => CsvFileWriter.Write(dialog.FileName, exportRows));
                    MessageBox.Show(this, "Exported " + (exportRows.Count - 1).ToString(CultureInfo.CurrentCulture) + " rows to:\n" + dialog.FileName,
                        "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "CSV export failed.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
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
                long received = 0;
                long sent = 0;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var stats = nic.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }

                var now = DateTime.UtcNow;
                if (lastAdapterReceived >= 0 && lastAdapterSent >= 0)
                {
                    double seconds = Math.Max(0.5, (now - lastAdapterSample).TotalSeconds);
                    double downKb = (received - lastAdapterReceived) / 1024d / seconds;
                    double upKb = (sent - lastAdapterSent) / 1024d / seconds;
                    bandwidthLabel.Text = "Total adapter bandwidth: Down " + downKb.ToString("0.0", CultureInfo.CurrentCulture) + " KB/s, Up " + upKb.ToString("0.0", CultureInfo.CurrentCulture) + " KB/s";
                }
                else
                {
                    bandwidthLabel.Text = "Total bandwidth: refresh again for speed sample";
                }

                lastAdapterReceived = received;
                lastAdapterSent = sent;
                lastAdapterSample = now;
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

            Run();
        }

        public static void Run()
        {
            TryEnableNativeDarkControls();
#pragma warning disable WFO5001
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
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

            SystemMemorySnapshot memory = NativeMemoryCollector.GetSnapshot();
            if (memory.PhysicalTotalBytes == 0 || memory.PhysicalAvailableBytes > memory.PhysicalTotalBytes ||
                memory.CommitTotalBytes > memory.CommitLimitBytes || memory.PhysicalLoadPercent < 0 || memory.PhysicalLoadPercent > 100 ||
                memory.ProcessCount == 0 || memory.ThreadCount == 0)
            {
                throw new InvalidOperationException("Native memory collector returned an invalid system snapshot.");
            }

            TestHistoryStore();
            TestAppAggregation();
            if (MainForm.RefreshIntervalMilliseconds(0) != 1000 || MainForm.RefreshIntervalMilliseconds(1) != 2000 ||
                MainForm.RefreshIntervalMilliseconds(2) != 5000 || MainForm.RefreshIntervalMilliseconds(3) != 15000)
            {
                throw new InvalidOperationException("Live monitoring interval mapping failed.");
            }

            using (var form = new MainForm())
            {
                if (Application.ProductVersion != "1.1.0-preview.5" || form.Text != "Better Task Manager v1.1.0-preview.5")
                {
                    throw new InvalidOperationException("Application version metadata and window title do not match 1.1.0-preview.5.");
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
                if (store.SaveSnapshot(new[] { row }, firstSeen.AddSeconds(31)) != 0) throw new InvalidOperationException("History store duplicated an unchanged connection.");
                if (store.SaveSnapshot(Array.Empty<NetworkRow>(), firstSeen.AddSeconds(62)) != 0) throw new InvalidOperationException("History store wrote an empty snapshot.");

                row.Timestamp = firstSeen.AddSeconds(93);
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
                new ProcessRow { Pid = 101, Name = "browser", Path = sharedPath, PrivateMb = 100.5, WorkingSetMb = 80.25 },
                new ProcessRow { Pid = 202, Name = "browser", Path = sharedPath, PrivateMb = 200.25, WorkingSetMb = 120.5 }
            };
            var connections = new List<NetworkRow>
            {
                new NetworkRow { Pid = 101, Process = "browser", Path = sharedPath, Protocol = "TCP" },
                new NetworkRow { Pid = 202, Process = "browser", Path = sharedPath, Protocol = "UDP" }
            };

            AppProfile profile = MainForm.BuildAppProfiles(processes, connections).Single();
            if (profile.Pids.Count != 2 || profile.ConnectionCount != 2 ||
                Math.Abs(profile.PrivateMb - 300.75) > 0.001 || Math.Abs(profile.RamMb - 200.75) > 0.001)
            {
                throw new InvalidOperationException("Grouped app aggregation does not match the sum of its per-process rows.");
            }
        }
    }
}
