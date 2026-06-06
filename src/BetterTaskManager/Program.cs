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

    public sealed class MainForm : Form
    {
        private static class Theme
        {
            public static readonly Color Window = Color.FromArgb(10, 12, 15);
            public static readonly Color Surface = Color.FromArgb(16, 19, 23);
            public static readonly Color SurfaceAlt = Color.FromArgb(20, 24, 29);
            public static readonly Color SurfaceRaised = Color.FromArgb(25, 30, 36);
            public static readonly Color Border = Color.FromArgb(45, 53, 63);
            public static readonly Color BorderStrong = Color.FromArgb(70, 84, 101);
            public static readonly Color Text = Color.FromArgb(239, 244, 250);
            public static readonly Color MutedText = Color.FromArgb(164, 174, 188);
            public static readonly Color Accent = Color.FromArgb(43, 94, 133);
            public static readonly Color AccentHover = Color.FromArgb(50, 109, 153);
            public static readonly Color AccentSelected = Color.FromArgb(37, 87, 128);
            public static readonly Color Good = Color.FromArgb(73, 201, 129);
            public static readonly Color Warning = Color.FromArgb(230, 183, 83);
            public static readonly Color Danger = Color.FromArgb(242, 101, 101);
            public static readonly Color Info = Color.FromArgb(125, 184, 232);
        }

        private readonly bool isAdmin;
        private readonly DataGridView appGrid;
        private readonly DataGridView appConnectionsGrid;
        private readonly Button appRefreshButton;
        private readonly Button appBlockButton;
        private readonly Button appUnblockButton;
        private readonly Label appFirewallCard;
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
        private readonly CheckBox autoRefreshCheck;
        private readonly Button restartAdminButton;
        private readonly Label statusLabel;
        private readonly TextBox filterBox;
        private readonly Button networkRefreshButton;
        private readonly Button blockButton;
        private readonly Button unblockButton;
        private readonly Button historyButton;
        private readonly Label networkStatusLabel;
        private readonly Label bandwidthLabel;
        private readonly DataGridView historyGrid;
        private readonly Panel historyTab;
        private readonly Button trimAllButton;
        private readonly Button clearStandbyButton;
        private readonly Button emptySystemButton;
        private readonly Label memoryStatusLabel;
        private readonly Panel pageHost;
        private readonly FlowLayoutPanel navBar;
        private readonly Panel appsTab;
        private readonly Panel networkTab;
        private Control activePage;
        private readonly Timer timer;
        private readonly Dictionary<DataGridView, Tuple<string, bool>> gridSortState = new Dictionary<DataGridView, Tuple<string, bool>>();

        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> lastCpu = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private List<ProcessRow> latestProcessRows = new List<ProcessRow>();
        private List<NetworkRow> latestNetworkRows = new List<NetworkRow>();
        private List<AppProfile> latestAppProfiles = new List<AppProfile>();
        private Dictionary<int, ProcessDetails> detailsCache = new Dictionary<int, ProcessDetails>();
        private bool detailsLoaded = false;
        private bool refreshingProcesses = false;
        private bool refreshingNetwork = false;
        private readonly Dictionary<string, string> firewallStatusCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string historyFolder;
        private readonly string historyPath;
        private DateTime lastHistoryWrite = DateTime.MinValue;
        private long lastAdapterReceived = -1;
        private long lastAdapterSent = -1;
        private DateTime lastAdapterSample = DateTime.MinValue;

        public MainForm()
        {
            Text = "Better Task Manager v1.0";
            Size = new Size(1560, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;

            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            historyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
            historyPath = Path.Combine(historyFolder, "network-history.csv");

            var rootShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            rootShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(rootShell);

            navBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0), Cursor = Cursors.Hand };
            rootShell.Controls.Add(navBar, 0, 0);

            pageHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };
            rootShell.Controls.Add(pageHost, 0, 1);

            appsTab = MakePage("Apps");
            var processTab = MakePage("Processes");
            networkTab = MakePage("Network");
            historyTab = MakePage("History");
            var memoryTab = MakePage("Memory");
            pageHost.Controls.AddRange(new Control[] { appsTab, processTab, networkTab, historyTab, memoryTab });

            var appsNavButton = MakeNavButton("Apps");
            var processesNavButton = MakeNavButton("Processes");
            var networkNavButton = MakeNavButton("Network");
            var memoryNavButton = MakeNavButton("Memory");
            navBar.Controls.AddRange(new Control[] { appsNavButton, processesNavButton, networkNavButton, memoryNavButton });
            appsNavButton.Click += (s, e) => ShowPage(appsTab);
            processesNavButton.Click += (s, e) => ShowPage(processTab);
            networkNavButton.Click += async (s, e) => { ShowPage(networkTab); await RefreshNetworkAsync(); };
            memoryNavButton.Click += (s, e) => ShowPage(memoryTab);

            var appShell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
            appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450));
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
                Tuple.Create("Connections", "Conn"),
                Tuple.Create("Ram", "Memory MB"),
                Tuple.Create("Path", "Path")
            });
            appGrid.Columns["App"].Width = 210;
            appGrid.Columns["Firewall"].Width = 72;
            appGrid.Columns["Connections"].Width = 58;
            appGrid.Columns["Ram"].Width = 100;
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
            appConnectionCard = MakeMetricCard("0", "Connections");
            appMemoryCard = MakeMetricCard("0 MB", "Private/Commit");
            appRamCard = MakeMetricCard("0 MB", "Memory");
            appFirewallCard = MakeMetricCard("Unknown", "Firewall");
            cardRow.Controls.AddRange(new Control[] { appConnectionCard, appMemoryCard, appRamCard, appFirewallCard });
            appRight.Controls.Add(cardRow, 0, 1);

            var appActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            appRefreshButton = MakeButton("Refresh Apps", 120);
            appBlockButton = MakeButton("Block App", 105);
            appUnblockButton = MakeButton("Unblock App", 115);
            appActions.Controls.AddRange(new Control[] { appRefreshButton, appBlockButton, appUnblockButton });
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

            var processPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            processPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processTab.Controls.Add(processPanel);

            var processToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            refreshButton = MakeButton("Refresh", 90);
            killButton = MakeButton("Force Kill", 100);
            trimSelectedButton = MakeButton("Trim Selected Memory", 160);
            loadDetailsButton = MakeButton("Load Users/Paths", 130);
            autoRefreshCheck = new CheckBox { Text = "Auto Refresh", Width = 105, Checked = false, Margin = new Padding(10, 8, 0, 0) };
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
            processToolbar.Controls.AddRange(new Control[] { refreshButton, killButton, trimSelectedButton, loadDetailsButton, autoRefreshCheck, restartAdminButton, filterLabel, filterBox, statusLabel });
            processPanel.Controls.Add(processToolbar, 0, 0);

            processGrid = NewGrid();
            AddColumns(processGrid, new[] {
                Tuple.Create("PID", "PID"),
                Tuple.Create("App", "Process"),
                Tuple.Create("User", "User"),
                Tuple.Create("CPU", "CPU %"),
                Tuple.Create("PrivateMB", "Private/Commit MB"),
                Tuple.Create("WorkingSetMB", "Memory MB"),
                Tuple.Create("PeakWorkingSetMB", "Peak RAM MB"),
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
            processPanel.Controls.Add(processGrid, 0, 1);

            var networkPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            networkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            networkTab.Controls.Add(networkPanel);

            var networkToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 6, 8, 4) };
            networkRefreshButton = MakeButton("Refresh", 90);
            blockButton = MakeButton("Block App", 100);
            unblockButton = MakeButton("Unblock App", 110);
            historyButton = MakeButton("Connection Log", 130);
            historyButton.Visible = false;
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
            networkToolbar.Controls.AddRange(new Control[] { networkRefreshButton, blockButton, unblockButton, historyButton, networkStatusLabel, bandwidthLabel });
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
            var reloadHistoryButton = MakeButton("Reload History", 120);
            var historyNote = new Label
            {
                Text = "Shows saved connection snapshots from the last 30 days.",
                AutoSize = true,
                Margin = new Padding(16, 9, 4, 0)
            };
            historyToolbar.Controls.AddRange(new Control[] { reloadHistoryButton, historyNote });
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
            memoryPanel.Controls.Add(new Label { Text = "Memory cleanup", Font = new Font("Segoe UI", 13, FontStyle.Bold), AutoSize = true });
            memoryPanel.Controls.Add(new Label { Text = "Troubleshooting actions for freeing cached or reserved RAM. Normal Windows caching is usually healthy.", AutoSize = true, Width = 900 });
            trimAllButton = MakeButton("Trim App Memory", 260);
            clearStandbyButton = MakeButton("Clear Standby Cache", 260);
            emptySystemButton = MakeButton("Release System Cache", 260);
            memoryStatusLabel = new Label { Text = "", AutoSize = true, Width = 900 };
            memoryPanel.Controls.AddRange(new Control[] { trimAllButton, clearStandbyButton, emptySystemButton, memoryStatusLabel });

            appRefreshButton.Click += async (s, e) => await RefreshAppsAsync();
            appSearchBox.TextChanged += (s, e) => FillAppGrid(latestAppProfiles);
            appGrid.SelectionChanged += (s, e) => ShowSelectedApp();
            appBlockButton.Click += async (s, e) => await BlockSelectedAppAsync(true);
            appUnblockButton.Click += async (s, e) => await BlockSelectedAppAsync(false);

            refreshButton.Click += async (s, e) => await RefreshProcessesAsync(false);
            loadDetailsButton.Click += async (s, e) => await LoadDetailsAndRefreshAsync();
            filterBox.TextChanged += async (s, e) => await RefreshProcessesAsync(false);
            killButton.Click += async (s, e) => await KillSelectedAsync();
            trimSelectedButton.Click += async (s, e) => await TrimSelectedAsync();
            restartAdminButton.Click += (s, e) => RestartAsAdmin();

            networkRefreshButton.Click += async (s, e) => await RefreshNetworkAsync();
            networkTab.Enter += async (s, e) => await RefreshNetworkAsync();
            blockButton.Click += async (s, e) => await BlockSelectedAsync(true);
            unblockButton.Click += async (s, e) => await BlockSelectedAsync(false);
            historyButton.Click += (s, e) => ShowHistory();
            reloadHistoryButton.Click += (s, e) => LoadHistoryGrid();

            trimAllButton.Click += async (s, e) => await TrimAllAsync();
            clearStandbyButton.Click += (s, e) => ClearStandby();
            emptySystemButton.Click += (s, e) => EmptySystemWorkingSets();

            timer = new Timer { Interval = 15000 };
            timer.Tick += async (s, e) =>
            {
                if (!autoRefreshCheck.Checked) return;
                await RefreshProcessesAsync(false);
                if (activePage == networkTab) await RefreshNetworkAsync();
            };

            Shown += async (s, e) =>
            {
                ApplyDarkTheme(this);
                ApplyNativeDarkTheme(this);
                ShowPage(appsTab);
                await RefreshAppsAsync();
                timer.Start();
            };

            ApplyDarkTheme(this);
            ShowPage(appsTab);
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
            var grid = new DataGridView
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
            if (columnName == "Connections") query = apps.OrderBy(a => a.ConnectionCount);
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

        private async Task RefreshAppsAsync()
        {
            appRefreshButton.Enabled = false;
            appTitleLabel.Text = "Loading apps...";
            appMetaLabel.Text = "Collecting processes and current connections";
            try
            {
                string filter = "";
                var cache = detailsCache;
                bool useDetails = detailsLoaded;
                var data = await Task.Run(() =>
                {
                    var processes = BuildProcessRows(filter, cache, useDetails);
                    var network = BuildNetworkRows();
                    var apps = BuildAppProfiles(processes, network);
                    return Tuple.Create(processes, network, apps);
                });

                latestProcessRows = data.Item1;
                latestNetworkRows = data.Item2;
                latestAppProfiles = data.Item3;
                FillAppGrid(latestAppProfiles);
                FillProcessGrid(latestProcessRows);
                FillNetworkGrid(latestNetworkRows);
                SaveNetworkHistory(latestNetworkRows);
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
            }
        }

        private static List<AppProfile> BuildAppProfiles(List<ProcessRow> processes, List<NetworkRow> network)
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

                    int index = appGrid.Rows.Add(app.Name, GetFirewallStatus(app.Path), app.ConnectionCount, app.RamMb.ToString("0.0", CultureInfo.CurrentCulture), app.Path);
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
            }
        }

        private void ShowSelectedApp()
        {
            if (appGrid.SelectedRows.Count == 0)
            {
                appTitleLabel.Text = "Select an app";
                appMetaLabel.Text = "";
                appConnectionCard.Text = "0\nConnections";
                appMemoryCard.Text = "0 MB\nPrivate/Commit";
                appRamCard.Text = "0 MB\nMemory";
                appFirewallCard.Text = "Unknown\nFirewall";
                appConnectionsGrid.Rows.Clear();
                return;
            }

            string name = Convert.ToString(appGrid.SelectedRows[0].Cells["App"].Value);
            string path = Convert.ToString(appGrid.SelectedRows[0].Cells["Path"].Value);
            var app = latestAppProfiles.FirstOrDefault(a =>
                (!string.IsNullOrWhiteSpace(path) && string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(path) && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));

            if (app == null) return;

            appTitleLabel.Text = app.Name;
            string pids = app.Pids.Count == 0 ? "No active PID" : "PID " + string.Join(", ", app.Pids.Take(8).Select(p => p.ToString(CultureInfo.InvariantCulture)));
            if (app.Pids.Count > 8) pids += " +" + (app.Pids.Count - 8).ToString(CultureInfo.InvariantCulture);
            appMetaLabel.Text = "Grouped app view    " + pids + "    " + (string.IsNullOrWhiteSpace(app.User) ? "User unknown" : app.User) + "    " + (string.IsNullOrWhiteSpace(app.Path) ? "Path unavailable" : app.Path);
            appConnectionCard.Text = app.ConnectionCount.ToString(CultureInfo.InvariantCulture) + "\nConnections";
            appMemoryCard.Text = app.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nPrivate/Commit";
            appRamCard.Text = app.RamMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nMemory";
            appFirewallCard.Text = GetFirewallStatus(app.Path) + "\nFirewall";

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
                await Task.Run(() => RunCommand("netsh.exe", "advfirewall firewall add rule name=\"" + rule + "\" dir=out program=\"" + path + "\" action=block profile=any"));
            }
            else
            {
                await Task.Run(() => RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"" + rule + "\""));
            }

            firewallStatusCache.Remove(path);
            await RefreshAppsAsync();
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

        private async Task RefreshProcessesAsync(bool forceDetails)
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
                bool useDetails = detailsLoaded || forceDetails;

                var rows = await Task.Run(() => BuildProcessRows(filter, cache, useDetails));
                latestProcessRows = rows;
                FillProcessGrid(rows);
                statusLabel.Text = isAdmin
                    ? (detailsLoaded ? "Running as administrator - users/paths loaded" : "Running as administrator")
                    : "Not administrator: some actions may fail";
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

        private List<ProcessRow> BuildProcessRows(string filter, Dictionary<int, ProcessDetails> cache, bool useDetails)
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
            processGrid.SuspendLayout();
            try
            {
                processGrid.Rows.Clear();
                foreach (var row in rows)
                {
                    processGrid.Rows.Add(row.Pid, row.Name, NormalizeDisplayText(row.User), row.Cpu.ToString("0.0", CultureInfo.CurrentCulture),
                        row.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.WorkingSetMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.PeakWorkingSetMb.ToString("0.0", CultureInfo.CurrentCulture),
                        row.Threads, row.Path);
                }
            }
            finally
            {
                processGrid.ResumeLayout();
            }
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
                await RefreshProcessesAsync(true);
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
                var rows = await Task.Run(() => BuildNetworkRows());
                latestNetworkRows = rows;
                FillNetworkGrid(rows);
                SaveNetworkHistory(rows);
                UpdateBandwidthLabel();
                networkStatusLabel.Text = "Loaded " + rows.Count + " network rows. Per-app bandwidth needs ETW/WFP collector.";
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

        private List<NetworkRow> BuildNetworkRows()
        {
            var now = DateTime.Now;
            var processNames = new Dictionary<int, string>();
            foreach (var p in Process.GetProcesses())
            {
                try { processNames[p.Id] = p.ProcessName; } catch { }
                finally { p.Dispose(); }
            }

            var rows = new List<NetworkRow>();
            var output = RunCommand("netstat.exe", "-ano");
            foreach (var raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (!(line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) || line.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                string protocol = parts[0];
                var local = SplitEndpoint(parts[1]);
                var remote = SplitEndpoint(parts[2]);
                string state = "";
                int pid = 0;

                if (protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
                {
                    state = NormalizeDisplayText(parts[3]);
                    int.TryParse(parts[4], out pid);
                }
                else if (protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
                {
                    state = "Listening";
                    int.TryParse(parts[3], out pid);
                }

                ProcessDetails details = null;
                detailsCache.TryGetValue(pid, out details);
                string name;
                processNames.TryGetValue(pid, out name);
                string path = details == null ? "" : details.Path;
                string user = details == null ? "" : details.User;
                if (string.IsNullOrWhiteSpace(path)) path = GetProcessPathFast(pid);
                if (string.IsNullOrWhiteSpace(user)) user = GetProcessUserFast(pid);
                user = NormalizeDisplayText(user);
                state = NormalizeConnectionState(state);

                rows.Add(new NetworkRow
                {
                    Timestamp = now,
                    Process = name ?? "",
                    Pid = pid,
                    User = user,
                    Protocol = protocol,
                    LocalAddress = local.Item1,
                    LocalPort = local.Item2,
                    RemoteAddress = remote.Item1,
                    RemotePort = remote.Item2,
                    State = state,
                    Path = path
                });
            }
            return rows.OrderBy(r => r.Process).ThenBy(r => r.Protocol).ThenBy(r => r.RemoteAddress).ToList();
        }

        private void FillNetworkGrid(List<NetworkRow> rows)
        {
            networkGrid.SuspendLayout();
            try
            {
                networkGrid.Rows.Clear();
                foreach (var row in rows)
                {
                    networkGrid.Rows.Add(row.Process, row.Pid, NormalizeDisplayText(row.User), row.Protocol, row.LocalAddress, row.LocalPort,
                        row.RemoteAddress, row.RemotePort, NormalizeConnectionState(row.State), row.Path);
                }
            }
            finally
            {
                networkGrid.ResumeLayout();
            }
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
            await Task.Run(() => RunCommand("taskkill.exe", "/PID " + pid.Value + " /F /T"));
            await RefreshProcessesAsync(false);
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
            await RefreshProcessesAsync(false);
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
            await RefreshProcessesAsync(false);
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
                await Task.Run(() => RunCommand("netsh.exe", "advfirewall firewall add rule name=\"" + rule + "\" dir=out program=\"" + path + "\" action=block profile=any"));
                MessageBox.Show(this, "Blocked outbound network access for this app.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                await Task.Run(() => RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"" + rule + "\""));
                MessageBox.Show(this, "Removed this app's Better Task Manager block rule.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowHistory()
        {
            Directory.CreateDirectory(historyFolder);
            LoadHistoryGrid();
            ShowPage(historyTab);
        }

        private void LoadHistoryGrid()
        {
            historyGrid.SuspendLayout();
            try
            {
                historyGrid.Rows.Clear();
                if (!File.Exists(historyPath)) return;

                foreach (var line in File.ReadLines(historyPath).Skip(1))
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Count < 11) continue;
                    historyGrid.Rows.Add(fields[0], fields[1], fields[2], fields[3], fields[4],
                        fields[5], fields[6], fields[7], fields[8], fields[9], fields[10]);
                }
            }
            finally
            {
                historyGrid.ResumeLayout();
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        private void RestartAsAdmin()
        {
            string script = Environment.GetCommandLineArgs().FirstOrDefault(a => a.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(script))
            {
                MessageBox.Show(this, "Could not find the script path to restart.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + script + "\"",
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
            Close();
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

        private void SaveNetworkHistory(List<NetworkRow> rows)
        {
            if ((DateTime.Now - lastHistoryWrite).TotalSeconds < 30) return;
            lastHistoryWrite = DateTime.Now;
            try
            {
                Directory.CreateDirectory(historyFolder);
                var cutoff = DateTime.Now.AddDays(-30);
                var lines = new List<string>();
                lines.Add("Timestamp,Process,PID,User,Protocol,LocalAddress,LocalPort,RemoteAddress,RemotePort,State,Path");
                if (File.Exists(historyPath))
                {
                    foreach (var line in File.ReadLines(historyPath).Skip(1))
                    {
                        var first = line.Split(',').FirstOrDefault();
                        DateTime timestamp;
                        if (DateTime.TryParse(first, out timestamp) && timestamp >= cutoff) lines.Add(line);
                    }
                }
                foreach (var row in rows)
                {
                    lines.Add(string.Join(",", new[]
                    {
                        Csv(row.Timestamp.ToString("s")),
                        Csv(row.Process),
                        Csv(row.Pid.ToString(CultureInfo.InvariantCulture)),
                        Csv(row.User),
                        Csv(row.Protocol),
                        Csv(row.LocalAddress),
                        Csv(row.LocalPort),
                        Csv(row.RemoteAddress),
                        Csv(row.RemotePort),
                        Csv(row.State),
                        Csv(row.Path)
                    }));
                }
                File.WriteAllLines(historyPath, lines, Encoding.UTF8);
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

        private static string Csv(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
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
            try
            {
                string cached;
                if (firewallStatusCache.TryGetValue(path, out cached)) return cached;
                string ruleName = RuleNameForPath(path);
                string result = RunCommand("netsh.exe", "advfirewall firewall show rule name=\"" + ruleName + "\"");
                string status = result.IndexOf(ruleName, StringComparison.OrdinalIgnoreCase) >= 0 ? "Blocked" : "Allowed";
                firewallStatusCache[path] = status;
                return status;
            }
            catch
            {
                return "Unknown";
            }
        }

        private static Tuple<string, string> SplitEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || endpoint == "*:*") return Tuple.Create("", "");
            endpoint = endpoint.Trim();
            if (endpoint.StartsWith("["))
            {
                int close = endpoint.LastIndexOf("]:", StringComparison.Ordinal);
                if (close > 0) return Tuple.Create(endpoint.Substring(1, close - 1), endpoint.Substring(close + 2));
            }
            int idx = endpoint.LastIndexOf(':');
            if (idx < 0) return Tuple.Create(endpoint, "");
            return Tuple.Create(endpoint.Substring(0, idx), endpoint.Substring(idx + 1));
        }

        private static string RunCommand(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                return output + (string.IsNullOrWhiteSpace(error) ? "" : "\n" + error);
            }
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
        public static void Main()
        {
            Run();
        }

        public static void Run()
        {
            TryEnableNativeDarkControls();
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
            using (var form = new MainForm())
            {
                return "Self-test OK. C# WinForms prototype compiled.";
            }
        }
    }
}
