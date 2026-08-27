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
using MessageBox = BetterTaskManager.LocalizedMessageBox;

namespace BetterTaskManager
{
    public static class NativeMethods
    {
        public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int TOKEN_QUERY = 0x0008;
        public const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const int SE_PRIVILEGE_ENABLED = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }

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

        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        public static extern bool LookupPrivilegeValue(string systemName, string name, out Luid luid);

        [DllImport("advapi32.dll", SetLastError=true)]
        public static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState,
            int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("psapi.dll", SetLastError=true)]
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

        public static bool TryEnablePrivilege(string privilegeName, out int errorCode)
        {
            errorCode = 0;
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    if (!OpenProcessToken(process.Handle, TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out tokenHandle))
                    {
                        errorCode = Marshal.GetLastWin32Error();
                        return false;
                    }
                }

                Luid luid;
                if (!LookupPrivilegeValue(null, privilegeName, out luid))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    return false;
                }

                var privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };
                bool adjusted = AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                errorCode = Marshal.GetLastWin32Error();
                return adjusted && errorCode == 0;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle);
            }
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

    internal sealed class MemoryTrimResult
    {
        public int Trimmed;
        public int Inaccessible;
        public int Exited;
        public int OtherFailed;
        public int Skipped;
    }

    internal enum MemoryTrimOutcome
    {
        Trimmed,
        Inaccessible,
        Exited,
        OtherFailed
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

    internal sealed class TightLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;

            switch (TextAlign)
            {
                case ContentAlignment.TopCenter: flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.Top; break;
                case ContentAlignment.TopRight: flags |= TextFormatFlags.Right | TextFormatFlags.Top; break;
                case ContentAlignment.MiddleLeft: flags |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleCenter: flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleRight: flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.BottomLeft: flags |= TextFormatFlags.Left | TextFormatFlags.Bottom; break;
                case ContentAlignment.BottomCenter: flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom; break;
                case ContentAlignment.BottomRight: flags |= TextFormatFlags.Right | TextFormatFlags.Bottom; break;
                default: flags |= TextFormatFlags.Left | TextFormatFlags.Top; break;
            }

            Rectangle textBounds = new Rectangle(
                ClientRectangle.Left + Padding.Left,
                ClientRectangle.Top + Padding.Top,
                Math.Max(0, ClientRectangle.Width - Padding.Horizontal),
                Math.Max(0, ClientRectangle.Height - Padding.Vertical));
            TextRenderer.DrawText(e.Graphics, Text ?? "", Font, textBounds, ForeColor, flags);
        }
    }

    internal sealed class VerticallyCenteredTextBox : TextBox
    {
        private const int EmSetRect = 0x00B3;
        private const int WmPaint = 0x000F;
        private string placeholderText = "";

        [StructLayout(LayoutKind.Sequential)]
        private struct EditRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr parameter, ref EditRectangle rectangle);

        public VerticallyCenteredTextBox()
        {
            AutoSize = false;
            Multiline = true;
            AcceptsReturn = false;
            WordWrap = false;
            ScrollBars = ScrollBars.None;
            Height = 30;
        }

        [System.ComponentModel.DefaultValue("")]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new string PlaceholderText
        {
            get { return placeholderText; }
            set
            {
                placeholderText = value ?? "";
                Invalidate();
            }
        }

        internal int TextTopOffset { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateFormattingRectangle();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateFormattingRectangle();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateFormattingRectangle();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (Text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                int selection = SelectionStart;
                Text = Text.Replace("\r", " ").Replace("\n", " ");
                SelectionStart = Math.Min(selection, TextLength);
                return;
            }
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmPaint && !Focused && TextLength == 0 && !string.IsNullOrEmpty(placeholderText))
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    var bounds = new Rectangle(5, TextTopOffset, Math.Max(0, ClientSize.Width - 10), Math.Max(0, ClientSize.Height - TextTopOffset));
                    TextRenderer.DrawText(graphics, placeholderText, Font, bounds, Color.FromArgb(166, 181, 201),
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void UpdateFormattingRectangle()
        {
            if (!IsHandleCreated || ClientSize.Width <= 8 || ClientSize.Height <= 0) return;
            int textHeight = TextRenderer.MeasureText("Ag", Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height;
            TextTopOffset = Math.Max(1, (ClientSize.Height - textHeight) / 2);
            var rectangle = new EditRectangle
            {
                Left = 4,
                Top = TextTopOffset,
                Right = Math.Max(5, ClientSize.Width - 4),
                Bottom = Math.Min(ClientSize.Height, TextTopOffset + textHeight + 1)
            };
            SendMessage(Handle, EmSetRect, IntPtr.Zero, ref rectangle);
            Invalidate();
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
        private const string FirewallStatusNoBlock = "Not blocked by BTM";

        private static class Theme
        {
            public static readonly Color Window = Color.FromArgb(27, 22, 41);
            public static readonly Color Surface = Color.FromArgb(35, 29, 52);
            public static readonly Color SurfaceAlt = Color.FromArgb(45, 37, 65);
            public static readonly Color SurfaceRaised = Color.FromArgb(57, 47, 79);
            public static readonly Color Border = Color.FromArgb(82, 67, 110);
            public static readonly Color BorderStrong = Color.FromArgb(111, 87, 153);
            public static readonly Color Text = Color.FromArgb(244, 240, 252);
            public static readonly Color MutedText = Color.FromArgb(187, 174, 207);
            public static readonly Color Accent = Color.FromArgb(139, 92, 246);
            public static readonly Color AccentHover = Color.FromArgb(167, 139, 250);
            public static readonly Color AccentSelected = Color.FromArgb(109, 75, 171);
            public static readonly Color Good = Color.FromArgb(91, 205, 160);
            public static readonly Color Warning = Color.FromArgb(235, 187, 92);
            public static readonly Color Danger = Color.FromArgb(239, 111, 117);
            public static readonly Color Info = Color.FromArgb(196, 181, 253);
        }

        private readonly bool isAdmin;
        private readonly bool hasSystemMemoryPrivilege;
        private readonly int systemMemoryPrivilegeError;
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
        private readonly VerticallyCenteredTextBox appSearchBox;
        private readonly Label appTitleLabel;
        private readonly Label appMetaLabel;
        private readonly Label appConnectionCard;
        private readonly Label appMemoryCard;
        private readonly Label appRamCard;
        private readonly DataGridView processGrid;
        private readonly DataGridView networkGrid;
        private readonly Button refreshButton;
        private readonly Button killButton;
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
        private readonly CheckBox historyRecordingCheck;
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
        private readonly FlowLayoutPanel memoryPanel;
        private readonly TableLayoutPanel memoryTrendPanel;
        private readonly PercentageTrendControl memoryCpuTrend;
        private readonly PercentageTrendControl memoryLoadTrend;
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
        private List<string> latestNetworkIssues = new List<string>();
        private List<AppProfile> latestAppProfiles = new List<AppProfile>();
        private List<string[]> latestHistoryRows = new List<string[]>();
        private List<string[]> visibleHistoryRows = new List<string[]>();
        private HashSet<int> processPidScope;
        private DateTime latestAppsSnapshot = DateTime.MinValue;
        private DateTime latestProcessSnapshot = DateTime.MinValue;
        private DateTime latestNetworkSnapshot = DateTime.MinValue;
        private Dictionary<int, ProcessDetails> detailsCache = new Dictionary<int, ProcessDetails>();
        private readonly object detailsCacheSync = new object();
        private bool refreshingApps = false;
        private bool firewallActionInProgress = false;
        private bool refreshingProcesses = false;
        private bool refreshingProcessDetails = false;
        private bool processActionInProgress = false;
        private bool refreshingNetwork = false;
        private bool updatingAppGrid = false;
        private bool settingProcessFilter = false;
        private bool loadingHistory = false;
        private bool refreshingHistory = false;
        private volatile bool historyRecordingEnabled = true;
        private bool memoryMaintenanceInProgress = false;
        private int historySortColumn = -1;
        private bool historySortAscending = true;
        private int historyPageStart = 0;
        private readonly Dictionary<string, string> firewallStatusCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private long firewallStateRevision = 0;
        private readonly NetworkHistoryStore historyStore;
        private readonly AppSettingsStore settingsStore;
        private readonly NativeCpuCollector systemCpuCollector = new NativeCpuCollector();
        private readonly NetworkBandwidthSampler bandwidthSampler = new NetworkBandwidthSampler();
        private readonly ToolTip shortcutToolTip;
        private readonly List<ContextMenuStrip> sectionContextMenus = new List<ContextMenuStrip>();
        private FormWindowState lastNonMinimizedWindowState = FormWindowState.Normal;

        public MainForm(bool skipInitialRefresh = false, string historyPath = null, string settingsPath = null)
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTaskManager");
            if (string.IsNullOrWhiteSpace(settingsPath)) settingsPath = Path.Combine(appDataFolder, "settings.json");
            settingsStore = new AppSettingsStore(settingsPath);
            shortcutToolTip = new ToolTip();
            AppSettings appSettings = settingsStore.Load();
            historyRecordingEnabled = appSettings.RecordHistory;
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
            hasSystemMemoryPrivilege = NativeMethods.TryEnablePrivilege("SeProfileSingleProcessPrivilege", out systemMemoryPrivilegeError);
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

            var appHeader = new TightLabel
            {
                Text = "Apps",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            appSearchBox = new VerticallyCenteredTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0) };
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
            appGrid.Columns["Firewall"].Width = 150;
            appGrid.Columns["Firewall"].MinimumWidth = 150;
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

            var appRight = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(24, 18, 24, 18), Margin = new Padding(0) };
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            appRight.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            appRight.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            appRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            appRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appShell.Controls.Add(appRight, 1, 0);

            var selectedHeader = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            selectedHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            selectedHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appTitleLabel = new TightLabel { Text = "Select an app", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 24, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0), Padding = new Padding(0) };
            appMetaLabel = new TightLabel { Text = "Refresh to load application activity", Dock = DockStyle.Fill, AutoEllipsis = true, Margin = new Padding(0), Padding = new Padding(0) };
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
            appFirewallDetailsLabel = new TightLabel
            {
                Text = "Select an app to inspect its Better Task Manager firewall rule.",
                Width = 360,
                Height = 30,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.MutedText,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            appActions.Controls.AddRange(new Control[] { appRefreshButton, exportAppsButton, appBlockButton, appUnblockButton, appViewProcessesButton, appOpenFolderButton, appCopyPathButton });
            appRight.Controls.Add(appActions, 0, 2);
            appRight.Controls.Add(appFirewallDetailsLabel, 0, 3);

            appRight.Controls.Add(new TightLabel { Text = "Connections", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0), Padding = new Padding(0) }, 0, 4);
            appConnectionsGrid = NewGrid();
            appConnectionsGrid.Margin = new Padding(0);
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
            appRight.Controls.Add(appConnectionsGrid, 0, 5);

            var processPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            processPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            processPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processTab.Controls.Add(processPanel);

            processToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 6, 8, 4) };
            refreshButton = MakeButton("Refresh", 90);
            killButton = MakeButton("Force Kill", 100);
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
            processToolbar.Controls.AddRange(new Control[] { refreshButton, killButton, exportProcessesButton, processOpenFolderButton, processCopyPathButton, filterLabel, filterBox, statusLabel });
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
            processGrid.Columns["PeakWorkingSetMB"].Width = 175;
            processGrid.Columns["PeakWorkingSetMB"].MinimumWidth = 175;
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
            networkGrid.Columns["LocalPort"].Width = 90;
            networkGrid.Columns["RemoteAddress"].Width = 180;
            networkGrid.Columns["RemotePort"].Width = 100;
            networkGrid.Columns["State"].Width = 110;
            networkGrid.Columns["Path"].Width = 500;
            LockGridColumns(networkGrid);
            networkPanel.Controls.Add(networkGrid, 0, 2);

            var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            historyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            historyTab.Controls.Add(historyPanel);
            historyToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 6, 8, 4) };
            reloadHistoryButton = MakeButton("Refresh", 100);
            var exportHistoryButton = MakeButton("Export CSV", 100);
            clearHistoryButton = MakeButton("Clear History", 105);
            historyPreviousButton = MakeButton("Previous", 80);
            historyNextButton = MakeButton("Next", 65);
            historyPreviousButton.Enabled = false;
            historyNextButton.Enabled = false;
            var historyFilterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(8, 9, 4, 0) };
            historyFilterBox = new TextBox { Width = 260, PlaceholderText = "App, PID, address, state, path..." };
            historyRecordingCheck = new CheckBox { Text = "Record history", AutoSize = true, Checked = historyRecordingEnabled, Margin = new Padding(12, 8, 4, 0) };
            historyNoteLabel = new Label
            {
                Text = "Shows new and changed connections from the last 30 days (newest first).",
                AutoSize = true,
                Margin = new Padding(12, 9, 4, 0)
            };
            historyToolbar.Controls.AddRange(new Control[] { reloadHistoryButton, exportHistoryButton, clearHistoryButton, historyPreviousButton, historyNextButton, historyFilterLabel, historyFilterBox, historyRecordingCheck, historyNoteLabel });
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

            memoryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
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

            memoryTrendPanel = new TableLayoutPanel { Width = 1400, Height = 180, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 10) };
            memoryTrendPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            memoryTrendPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            memoryCpuTrend = new PercentageTrendControl { Dock = DockStyle.Fill, Title = "System CPU - last 60 samples", LineColor = Theme.AccentHover, Margin = new Padding(0, 0, 7, 0), AccessibleName = "System CPU trend" };
            memoryLoadTrend = new PercentageTrendControl { Dock = DockStyle.Fill, Title = "Physical RAM Load - last 60 samples", LineColor = Theme.Good, Margin = new Padding(7, 0, 0, 0), AccessibleName = "Physical RAM load trend" };
            memoryTrendPanel.Controls.Add(memoryCpuTrend, 0, 0);
            memoryTrendPanel.Controls.Add(memoryLoadTrend, 1, 0);
            memoryPanel.Controls.Add(memoryTrendPanel);

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
            memoryStatusLabel = new Label
            {
                Text = hasSystemMemoryPrivilege ? "System-memory privilege active." : SystemMemoryPrivilegeUnavailableText(),
                ForeColor = hasSystemMemoryPrivilege ? Theme.Good : Theme.Warning,
                AutoSize = true,
                Width = 1400
            };
            memoryPanel.Controls.Add(memoryStatusLabel);
            memoryTab.Resize += (s, e) => UpdateMemoryTrendWidth();

            ConfigureSectionContextMenus(
                exportAppsButton,
                exportProcessesButton,
                exportNetworkButton,
                exportHistoryButton);

            shortcutToolTip.SetToolTip(liveMonitoringCheck, "Toggle Live monitoring (Ctrl+L)");
            shortcutToolTip.SetToolTip(appsNavButton, "Open Apps (Ctrl+1)");
            shortcutToolTip.SetToolTip(processesNavButton, "Open Processes (Ctrl+2)");
            shortcutToolTip.SetToolTip(networkNavButton, "Open Network (Ctrl+3)");
            shortcutToolTip.SetToolTip(historyNavButton, "Open History (Ctrl+4)");
            shortcutToolTip.SetToolTip(memoryNavButton, "Open Memory (Ctrl+5)");
            shortcutToolTip.SetToolTip(adminStatusLabel, hasSystemMemoryPrivilege
                ? "This process is elevated and the required system-memory privilege is active."
                : SystemMemoryPrivilegeUnavailableText());
            shortcutToolTip.SetToolTip(clearStandbyButton, hasSystemMemoryPrivilege
                ? "Purge the Windows standby list for troubleshooting."
                : SystemMemoryPrivilegeUnavailableText());
            shortcutToolTip.SetToolTip(emptySystemButton, hasSystemMemoryPrivilege
                ? "Empty system working sets for troubleshooting."
                : SystemMemoryPrivilegeUnavailableText());
            string firewallActionTip = isAdmin
                ? "Modify Better Task Manager's outbound block rule for the selected executable."
                : "Modify Better Task Manager's outbound block rule; Windows will request administrator approval.";
            foreach (Button button in new[] { appBlockButton, appUnblockButton, blockButton, unblockButton })
            {
                shortcutToolTip.SetToolTip(button, firewallActionTip);
            }
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

            refreshButton.Click += async (s, e) => await RefreshProcessesManuallyAsync();
            filterBox.TextChanged += (s, e) =>
            {
                if (settingProcessFilter) return;
                processPidScope = null;
                FillProcessGridFromCache();
            };
            killButton.Click += async (s, e) => await KillSelectedAsync();
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
            networkGrid.SelectionChanged += (s, e) =>
            {
                UpdateExecutablePathActions();
                UpdateFirewallActionButtons();
            };
            networkOpenFolderButton.Click += (s, e) => OpenSelectedExecutableFolder();
            networkCopyPathButton.Click += async (s, e) => await CopySelectedExecutablePathAsync();
            reloadHistoryButton.Click += async (s, e) => await LoadHistoryGridAsync();
            exportHistoryButton.Click += async (s, e) => await ExportHistoryAsync();
            clearHistoryButton.Click += async (s, e) => await ClearHistoryAsync();
            historyPreviousButton.Click += (s, e) => MoveHistoryPage(-1);
            historyNextButton.Click += (s, e) => MoveHistoryPage(1);
            historyFilterBox.TextChanged += (s, e) => FillHistoryGrid(true);
            historyRecordingCheck.CheckedChanged += (s, e) =>
            {
                historyRecordingEnabled = historyRecordingCheck.Checked;
                UpdateHistoryRecordingStatus();
            };

            trimAllButton.Click += async (s, e) => await TrimAllAsync();
            clearStandbyButton.Click += async (s, e) => await ClearStandbyAsync();
            emptySystemButton.Click += async (s, e) => await EmptySystemWorkingSetsAsync();
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
            FormClosed += (s, e) =>
            {
                shortcutToolTip.Dispose();
                foreach (ContextMenuStrip menu in sectionContextMenus) menu.Dispose();
            };
            Resize += (s, e) =>
            {
                if (WindowState != FormWindowState.Minimized) lastNonMinimizedWindowState = WindowState;
            };

            RestoreColumnWidths(appSettings.ColumnWidths);
            UiText.ApplyTo(this, shortcutToolTip, sectionContextMenus);
            memoryCpuTrend.Title = UiText.Translate(memoryCpuTrend.Title);
            memoryLoadTrend.Title = UiText.Translate(memoryLoadTrend.Title);
            memoryCpuTrend.AccessibleName = UiText.Translate(memoryCpuTrend.AccessibleName);
            memoryLoadTrend.AccessibleName = UiText.Translate(memoryLoadTrend.AccessibleName);
            ApplyDarkTheme(this);
            ApplyPrivilegeState();
            UpdateMemoryTrendWidth();
            ShowPage(appsTab);
            ShowSelectedApp();
            UpdateExecutablePathActions();
        }

        private void ApplyPrivilegeState()
        {
            restartAdminButton.Visible = !isAdmin;
            adminStatusLabel.Text = isAdmin
                ? (hasSystemMemoryPrivilege ? "Administrator · memory privilege ready" : "Administrator · memory privilege unavailable")
                : "Standard mode";
            adminStatusLabel.ForeColor = hasSystemMemoryPrivilege ? Theme.Good : (isAdmin ? Theme.Warning : Theme.MutedText);
            UpdateFirewallActionButtons();
            trimAllButton.Enabled = !memoryMaintenanceInProgress;
            clearStandbyButton.Enabled = hasSystemMemoryPrivilege && !memoryMaintenanceInProgress;
            emptySystemButton.Enabled = hasSystemMemoryPrivilege && !memoryMaintenanceInProgress;
        }

        private void ConfigureSectionContextMenus(Button exportAppsButton, Button exportProcessesButton,
            Button exportNetworkButton, Button exportHistoryButton)
        {
            ContextMenuStrip appsMenu = CreateSectionActionMenu(appRefreshButton, exportAppsButton, appBlockButton,
                appUnblockButton, appViewProcessesButton, appOpenFolderButton, appCopyPathButton);
            AssignSectionContextMenu(appsMenu, appsTab, appGrid, appConnectionsGrid, appMetricCards, appActions);

            ContextMenuStrip processesMenu = CreateSectionActionMenu(refreshButton, killButton, exportProcessesButton,
                processOpenFolderButton, processCopyPathButton);
            AssignSectionContextMenu(processesMenu, processTab, processGrid, processToolbar, processSummaryLabel);

            ContextMenuStrip networkMenu = CreateSectionActionMenu(networkRefreshButton, blockButton, unblockButton,
                exportNetworkButton, networkOpenFolderButton, networkCopyPathButton);
            AssignSectionContextMenu(networkMenu, networkTab, networkGrid, networkToolbar, networkStatusLabel, bandwidthLabel);

            ContextMenuStrip historyMenu = CreateSectionActionMenu(reloadHistoryButton, exportHistoryButton, clearHistoryButton,
                historyPreviousButton, historyNextButton);
            var recordHistoryItem = new ToolStripMenuItem("Record history")
            {
                CheckOnClick = false,
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text
            };
            recordHistoryItem.Click += (s, e) => historyRecordingCheck.Checked = !historyRecordingCheck.Checked;
            historyMenu.Items.Add(new ToolStripSeparator());
            historyMenu.Items.Add(recordHistoryItem);
            historyMenu.Opening += (s, e) =>
            {
                recordHistoryItem.Checked = historyRecordingCheck.Checked;
                recordHistoryItem.Enabled = historyRecordingCheck.Enabled;
            };
            AssignSectionContextMenu(historyMenu, historyTab, historyList, historyToolbar, historyNoteLabel);

            ContextMenuStrip memoryMenu = CreateSectionActionMenu(memoryRefreshButton, trimAllButton, clearStandbyButton, emptySystemButton);
            AssignSectionContextMenu(memoryMenu, memoryTab, memoryPanel, memoryTrendPanel, memoryCpuTrend, memoryLoadTrend);

            foreach (DataGridView grid in new[] { appGrid, appConnectionsGrid, processGrid, networkGrid })
            {
                grid.CellMouseDown += SelectRightClickedGridRow;
            }
        }

        private ContextMenuStrip CreateSectionActionMenu(params Button[] buttons)
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text,
                ShowImageMargin = false
            };
            foreach (Button button in buttons.Where(button => button != null))
            {
                var item = new ToolStripMenuItem(button.Text)
                {
                    Tag = button,
                    BackColor = Theme.SurfaceRaised,
                    ForeColor = Theme.Text
                };
                item.Click += (s, e) => ((Button)((ToolStripMenuItem)s).Tag).PerformClick();
                menu.Items.Add(item);
            }
            menu.Opening += (s, e) =>
            {
                foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
                {
                    Button source = item.Tag as Button;
                    if (source != null) item.Enabled = source.Enabled && source.Visible;
                }
            };
            sectionContextMenus.Add(menu);
            return menu;
        }

        private static void AssignSectionContextMenu(ContextMenuStrip menu, params Control[] controls)
        {
            foreach (Control control in controls.Where(control => control != null)) control.ContextMenuStrip = menu;
        }

        private static void SelectRightClickedGridRow(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;
            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;
            int columnIndex = e.ColumnIndex >= 0 ? e.ColumnIndex : 0;
            if (columnIndex < grid.Columns.Count) grid.CurrentCell = grid.Rows[e.RowIndex].Cells[columnIndex];
        }

        private void UpdateMemoryTrendWidth()
        {
            int availableWidth = Math.Max(520, memoryTab.ClientSize.Width - memoryPanel.Padding.Horizontal - 8);
            memoryTrendPanel.Width = availableWidth;
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
            else if (activePage == processTab) await RefreshProcessesManuallyAsync();
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
                    RecordHistory = historyRecordingEnabled,
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
                if (widths.TryGetValue(prefix + "." + column.Name, out width))
                {
                    column.Width = Math.Max(column.MinimumWidth, ClampColumnWidth(width));
                }
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
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            grid.ColumnHeaderMouseClick -= GridColumnHeaderMouseClick;
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.CellPainting -= GridHeaderCellPainting;
            grid.CellPainting += GridHeaderCellPainting;
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
            RefreshSortIndicator(grid);
        }

        private void GridHeaderCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            SortOrder order = CurrentSortOrder(grid, grid.Columns[e.ColumnIndex].Name);
            if (order != SortOrder.None)
            {
                int centerX = e.CellBounds.Right - 11;
                int centerY = e.CellBounds.Top + (e.CellBounds.Height / 2);
                Point[] triangle = order == SortOrder.Ascending
                    ? new[] { new Point(centerX, centerY - 6), new Point(centerX - 6, centerY + 4), new Point(centerX + 6, centerY + 4) }
                    : new[] { new Point(centerX - 6, centerY - 4), new Point(centerX + 6, centerY - 4), new Point(centerX, centerY + 6) };
                using (var brush = new SolidBrush(Theme.AccentHover)) e.Graphics.FillPolygon(brush, triangle);
                using (var pen = new Pen(Color.White, 1)) e.Graphics.DrawPolygon(pen, triangle);
            }
            e.Handled = true;
        }

        private SortOrder CurrentSortOrder(DataGridView grid, string columnName)
        {
            Tuple<string, bool> state;
            if (!gridSortState.TryGetValue(grid, out state) || !string.Equals(state.Item1, columnName, StringComparison.Ordinal))
            {
                return SortOrder.None;
            }
            return state.Item2 ? SortOrder.Ascending : SortOrder.Descending;
        }

        private static void RefreshSortIndicator(DataGridView grid)
        {
            grid.Invalidate(new Rectangle(0, 0, grid.ClientSize.Width, grid.ColumnHeadersHeight));
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
            UpdateFirewallActionButtons();
            appTitleLabel.Text = "Loading apps...";
            appMetaLabel.Text = "Collecting processes and connections";
            appMetaLabel.ForeColor = Theme.Info;
            try
            {
                Dictionary<int, ProcessDetails> cache;
                lock (detailsCacheSync) cache = detailsCache;
                var data = await RunSnapshotCollectionAsync(appsTab, () =>
                {
                    DateTime snapshotTime = DateTime.Now;
                    var processes = BuildProcessRows(cache);
                    var networkIssues = new List<string>();
                    var network = BuildNetworkRows(processes, networkIssues);
                    var apps = BuildAppProfiles(processes, network);
                    SaveNetworkHistory(network);
                    return Tuple.Create(processes, network, apps, snapshotTime, networkIssues);
                });
                if (data == null) return;

                latestProcessRows = data.Item1;
                latestNetworkRows = data.Item2;
                latestNetworkIssues = data.Item5;
                latestAppProfiles = data.Item3;
                latestAppsSnapshot = data.Item4;
                latestProcessSnapshot = data.Item4;
                latestNetworkSnapshot = data.Item4;
                FillAppGridFromCache();
                UpdateBandwidthLabel();
                ShowSelectedApp();
                MarkLiveRefreshSuccess();

                if (refreshFirewall)
                {
                    DateTime requestedSnapshot = data.Item4;
                    long requestedRevision = firewallStateRevision;
                    appFirewallDetailsLabel.Text = "Refreshing Better Task Manager firewall rule state...";
                    appFirewallDetailsLabel.ForeColor = Theme.Info;
                    try
                    {
                        Dictionary<string, string> firewall = await Task.Run(() => LoadFirewallStatuses(data.Item3));
                        if (ShouldApplyFirewallResult(latestAppsSnapshot, requestedSnapshot, firewallStateRevision, requestedRevision))
                        {
                            firewallStatusCache.Clear();
                            foreach (var pair in firewall) firewallStatusCache[pair.Key] = pair.Value;
                            FillAppGridFromCache();
                            ShowSelectedApp();
                        }
                    }
                    catch (Exception ex)
                    {
                        appFirewallDetailsLabel.Text = "Firewall status refresh failed: " + ex.Message;
                        appFirewallDetailsLabel.ForeColor = Theme.Warning;
                    }
                }
            }
            catch (Exception ex)
            {
                appTitleLabel.Text = "Refresh failed";
                appMetaLabel.Text = ex.Message;
                appMetaLabel.ForeColor = Theme.Danger;
                MarkLiveRefreshFailure();
            }
            finally
            {
                appRefreshButton.Enabled = true;
                refreshingApps = false;
                UpdateFirewallActionButtons();
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

        internal static bool ShouldApplyFirewallResult(DateTime currentSnapshot, DateTime requestedSnapshot, long currentRevision, long requestedRevision)
        {
            return currentSnapshot == requestedSnapshot && currentRevision == requestedRevision;
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
                    if (!string.IsNullOrWhiteSpace(file)) return file.Trim();
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(processName)) return "Unknown App";
            int titleSeparator = processName.IndexOf(" - ", StringComparison.Ordinal);
            return (titleSeparator > 0 ? processName.Substring(0, titleSeparator) : processName).Trim();
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
                UiText.Translate(firewallStatus ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                appMetaLabel.Text = AppNetworkCompletenessText(latestNetworkIssues);
                appMetaLabel.ForeColor = latestNetworkIssues.Count == 0 ? Theme.MutedText : Theme.Warning;
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
                UpdateFirewallActionButtons();
                return;
            }

            AppProfile app = SelectedAppProfile();
            if (app == null) return;

            appTitleLabel.Text = (app.Name ?? "").Trim();
            string pids = app.Pids.Count == 0 ? "No active PID" : "PID " + string.Join(", ", app.Pids.Take(8).Select(p => p.ToString(CultureInfo.InvariantCulture)));
            if (app.Pids.Count > 8) pids += " +" + (app.Pids.Count - 8).ToString(CultureInfo.InvariantCulture);
            appMetaLabel.Text = SnapshotLabel(latestAppsSnapshot) + "    " + app.Pids.Count.ToString(CultureInfo.CurrentCulture) + " processes aggregated    " + pids +
                "    CPU " + AppCpuSummaryText(app) + "    " +
                (string.IsNullOrWhiteSpace(app.User) ? "User unknown" : app.User) + "    " + (string.IsNullOrWhiteSpace(app.Path) ? "Path unavailable" : app.Path) +
                (latestNetworkIssues.Count == 0 ? "" : "    " + AppNetworkCompletenessText(latestNetworkIssues));
            appMetaLabel.ForeColor = latestNetworkIssues.Count == 0 ? Theme.MutedText : Theme.Warning;
            appConnectionCard.Text = app.ConnectionCount.ToString(CultureInfo.InvariantCulture) + "\nGroup Connections";
            appMemoryCard.Text = app.PrivateMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Private Bytes";
            appRamCard.Text = app.RamMb.ToString("0.0", CultureInfo.CurrentCulture) + " MB\nSum Working Set";
            string firewallStatus = GetFirewallStatus(app.Path);
            appFirewallCard.Text = firewallStatus + "\nFirewall";
            appFirewallDetailsLabel.Text = FirewallExplanation(app.Path, firewallStatus);
            appFirewallDetailsLabel.ForeColor = firewallStatus == FirewallStatusBlocked
                ? Theme.Danger
                : firewallStatus == FirewallStatusNoBlock ? Theme.MutedText : Theme.Warning;
            UpdateFirewallActionButtons();
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

            if (block && MessageBox.Show(this, "Block outbound network access for:\n" + path, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!BeginFirewallAction()) return;
            try
            {
                if (!await TryApplyFirewallRuleAsync(path, block)) return;

                firewallStatusCache[path] = block ? FirewallStatusBlocked : FirewallStatusNoBlock;
                firewallStateRevision++;
                await RefreshAppsAsync(false);
            }
            finally
            {
                EndFirewallAction();
            }
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

        private async Task RefreshProcessesAsync(bool automatic = false, bool identitiesRebuilt = false)
        {
            if (refreshingProcesses) return;
            refreshingProcesses = true;
            refreshButton.Enabled = false;
            UpdateExecutablePathActions();
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
                    ? (identitiesRebuilt ? "Running as administrator - identities refreshed" : "Running as administrator")
                    : (identitiesRebuilt ? "Standard mode - identities refreshed where accessible" : "Standard mode: protected identities may be unavailable"));
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
                UpdateExecutablePathActions();
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
                            if (lastCpu.TryGetValue(pid, out old) && ProcessStartTimesMatch(old.Item3, processStartTimeUtcTicks))
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
            return cached == null || ProcessStartTimesMatch(cached.ProcessStartTimeUtcTicks, processStartTimeUtcTicks);
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

        private async Task RefreshProcessesManuallyAsync()
        {
            if (refreshingProcesses || refreshingProcessDetails) return;
            refreshingProcessDetails = true;
            refreshButton.Enabled = false;
            UpdateExecutablePathActions();
            statusLabel.Text = "Refreshing processes, usernames, and executable paths...";
            statusLabel.ForeColor = Theme.Warning;
            try
            {
                Dictionary<int, ProcessDetails> loadedDetails = await RunSnapshotCollectionAsync(processTab, () => LoadProcessDetails());
                if (loadedDetails == null) return;
                lock (detailsCacheSync) detailsCache = loadedDetails;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Process identity refresh failed";
                statusLabel.ForeColor = Theme.Danger;
                MessageBox.Show(this, ex.Message, "Process refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                refreshingProcessDetails = false;
                refreshButton.Enabled = true;
                UpdateExecutablePathActions();
            }
            await RefreshProcessesAsync(false, true);
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
            UpdateExecutablePathActions();
            UpdateFirewallActionButtons();
            networkStatusLabel.Text = "Loading network connections...";
            networkStatusLabel.ForeColor = Theme.Warning;
            try
            {
                var result = await RunSnapshotCollectionAsync(networkTab, () =>
                {
                    var issues = new List<string>();
                    var networkRows = BuildNetworkRows(null, issues);
                    SaveNetworkHistory(networkRows);
                    return Tuple.Create(networkRows, issues);
                });
                if (result == null) return;
                var rows = result.Item1;
                latestNetworkRows = rows;
                latestNetworkIssues = result.Item2;
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
                UpdateExecutablePathActions();
                UpdateFirewallActionButtons();
            }
        }

        private List<NetworkRow> BuildNetworkRows(IEnumerable<ProcessRow> knownProcessRows = null, List<string> issues = null)
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

            NativeNetworkSnapshot nativeSnapshot = NativeNetworkCollector.GetSnapshot();
            if (issues != null) issues.AddRange(nativeSnapshot.Issues);
            var rows = new List<NetworkRow>();
            foreach (var connection in nativeSnapshot.Connections)
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
                " connections shown. Per-app bandwidth needs ETW/WFP collector." + NetworkIssueSummary(latestNetworkIssues);
            networkStatusLabel.ForeColor = latestNetworkIssues.Count == 0 ? Theme.MutedText : Theme.Warning;
        }

        internal static string NetworkIssueSummary(IEnumerable<string> issues)
        {
            var list = (issues ?? Enumerable.Empty<string>()).Where(issue => !string.IsNullOrWhiteSpace(issue)).ToList();
            if (list.Count == 0) return "";
            return " Collector warning" + (list.Count == 1 ? "" : "s") + ": " + string.Join(" | ", list.Take(2)) + (list.Count > 2 ? " | +" + (list.Count - 2).ToString(CultureInfo.CurrentCulture) : "");
        }

        internal static string AppNetworkCompletenessText(IEnumerable<string> issues)
        {
            int count = (issues ?? Enumerable.Empty<string>()).Count(issue => !string.IsNullOrWhiteSpace(issue));
            return count == 0 ? "" : "Network data partial: " + count.ToString(CultureInfo.CurrentCulture) + " native table warning" + (count == 1 ? "" : "s") + ".";
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
                UiText.Translate(NormalizeConnectionState(row.State)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
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

        private ProcessRow SelectedProcessRow()
        {
            int? pid = SelectedPid(processGrid);
            return pid == null ? null : latestProcessRows.FirstOrDefault(row => row.Pid == pid.Value);
        }

        internal static bool ProcessStartTimesMatch(long snapshotStartTimeUtcTicks, long currentStartTimeUtcTicks)
        {
            return snapshotStartTimeUtcTicks == 0 || currentStartTimeUtcTicks == 0 || snapshotStartTimeUtcTicks == currentStartTimeUtcTicks;
        }

        internal static bool CanForceKillProcess(int selectedPid, int currentProcessId)
        {
            return selectedPid > 0 && selectedPid != currentProcessId;
        }

        private bool SelectedProcessInstanceIsCurrent(ProcessRow selected)
        {
            try
            {
                using (var process = Process.GetProcessById(selected.Pid))
                {
                    if (ProcessStartTimesMatch(selected.ProcessStartTimeUtcTicks, SafeProcessStartTimeUtcTicks(process))) return true;
                }
                MessageBox.Show(this, "This PID now belongs to a different process. Refresh and select the process again.",
                    "Stale process selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, "The selected process has already exited. Refresh the Process view.",
                    "Process exited", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
        }

        private async Task KillSelectedAsync()
        {
            ProcessRow selected = SelectedProcessRow();
            if (selected == null)
            {
                MessageBox.Show(this, "Select a process first.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!CanForceKillProcess(selected.Pid, Environment.ProcessId))
            {
                MessageBox.Show(this, "Better Task Manager cannot force-kill its own process.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!SelectedProcessInstanceIsCurrent(selected)) return;

            string identity = selected.Name + "\nPID " + selected.Pid.ToString(CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(selected.Path) ? "" : "\n" + selected.Path);
            if (MessageBox.Show(this, "Force kill this process and its child processes?\n\n" + identity,
                "Confirm force kill", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!BeginProcessAction()) return;
            try
            {
                await Task.Run(() =>
                {
                    using (var process = Process.GetProcessById(selected.Pid))
                    {
                        long currentStartTime = SafeProcessStartTimeUtcTicks(process);
                        if (!ProcessStartTimesMatch(selected.ProcessStartTimeUtcTicks, currentStartTime))
                        {
                            throw new InvalidOperationException("The PID now belongs to a different process. Refresh and select it again.");
                        }
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                });
                await RefreshProcessesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Force-killing PID " + selected.Pid + " failed.\n\n" + ex.Message, "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                EndProcessAction();
            }
        }

        private async Task TrimAllAsync()
        {
            if (MessageBox.Show(this, "Trim memory for all accessible apps? Better Task Manager itself is excluded.\n\nThis can reduce visible RAM use, but apps may reload data afterward.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!BeginMemoryMaintenance()) return;
            try
            {
                int currentProcessId = Environment.ProcessId;
                MemoryTrimResult result = await Task.Run(() =>
                {
                    var summary = new MemoryTrimResult();
                    foreach (var process in Process.GetProcesses())
                    {
                        try
                        {
                            if (!ShouldTrimProcess(process.Id, currentProcessId))
                            {
                                summary.Skipped++;
                                continue;
                            }
                            switch (TryTrimWorkingSet(process))
                            {
                                case MemoryTrimOutcome.Trimmed: summary.Trimmed++; break;
                                case MemoryTrimOutcome.Inaccessible: summary.Inaccessible++; break;
                                case MemoryTrimOutcome.Exited: summary.Exited++; break;
                                default: summary.OtherFailed++; break;
                            }
                        }
                        catch { summary.OtherFailed++; }
                        finally { process.Dispose(); }
                    }
                    return summary;
                });
                memoryStatusLabel.ForeColor = result.Inaccessible == 0 && result.OtherFailed == 0 ? Theme.Good : Theme.Warning;
                memoryStatusLabel.Text = MemoryTrimSummaryText(result, isAdmin);
                RefreshMemoryPage();
            }
            catch (Exception ex)
            {
                memoryStatusLabel.ForeColor = Theme.Danger;
                memoryStatusLabel.Text = "Working-set trim failed: " + ex.Message;
            }
            finally
            {
                EndMemoryMaintenance();
            }
        }

        internal static bool ShouldTrimProcess(int processId, int currentProcessId)
        {
            return processId > 0 && processId != currentProcessId;
        }

        private static MemoryTrimOutcome TryTrimWorkingSet(Process process)
        {
            try
            {
                if (NativeMethods.EmptyWorkingSet(process.Handle)) return MemoryTrimOutcome.Trimmed;
                return MemoryTrimOutcomeForWin32Error(Marshal.GetLastWin32Error());
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return MemoryTrimOutcomeForWin32Error(ex.NativeErrorCode);
            }
            catch (InvalidOperationException)
            {
                return MemoryTrimOutcome.Exited;
            }
            catch (ArgumentException)
            {
                return MemoryTrimOutcome.Exited;
            }
            catch
            {
                return MemoryTrimOutcome.OtherFailed;
            }
        }

        internal static MemoryTrimOutcome MemoryTrimOutcomeForWin32Error(int errorCode)
        {
            if (errorCode == 5 || errorCode == 1314) return MemoryTrimOutcome.Inaccessible;
            if (errorCode == 6 || errorCode == 87 || errorCode == 1168) return MemoryTrimOutcome.Exited;
            return MemoryTrimOutcome.OtherFailed;
        }

        internal static string MemoryTrimSummaryText(MemoryTrimResult result, bool administrator)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            string explanation = result.Inaccessible == 0
                ? ""
                : administrator
                    ? " Protected services and security processes can still refuse trimming."
                    : " Restart as Admin may reduce denials; protected services can still refuse.";
            return "Working-set trim: " + result.Trimmed.ToString(CultureInfo.CurrentCulture) + " trimmed; " +
                result.Inaccessible.ToString(CultureInfo.CurrentCulture) + " protected/access denied; " +
                result.Exited.ToString(CultureInfo.CurrentCulture) + " exited during scan; " +
                result.OtherFailed.ToString(CultureInfo.CurrentCulture) + " other failures; " +
                result.Skipped.ToString(CultureInfo.CurrentCulture) + " skipped (System/BTM)." + explanation;
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
                if (cpuSnapshot.SampleAvailable) memoryCpuTrend.AddSample(cpuSnapshot.UsagePercent);
                memoryLoadTrend.AddSample(snapshot.PhysicalLoadPercent);
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

        private bool BeginMemoryMaintenance()
        {
            if (memoryMaintenanceInProgress) return false;
            memoryMaintenanceInProgress = true;
            ApplyPrivilegeState();
            return true;
        }

        private void EndMemoryMaintenance()
        {
            memoryMaintenanceInProgress = false;
            ApplyPrivilegeState();
        }

        private async Task ClearStandbyAsync()
        {
            if (!hasSystemMemoryPrivilege)
            {
                MessageBox.Show(this, SystemMemoryPrivilegeUnavailableText(), "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Clear Windows standby cache?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunSystemMemoryActionAsync("Clearing standby cache...", "Clear standby cache", () => NativeMethods.PurgeStandbyList());
        }

        private async Task EmptySystemWorkingSetsAsync()
        {
            if (!hasSystemMemoryPrivilege)
            {
                MessageBox.Show(this, SystemMemoryPrivilegeUnavailableText(), "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Release system cache/working sets? Use this only for troubleshooting memory pressure.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunSystemMemoryActionAsync("Releasing system cache/working sets...", "System working set cleanup", () => NativeMethods.EmptySystemWorkingSets());
        }

        private async Task RunSystemMemoryActionAsync(string progressText, string resultPrefix, Func<int> action)
        {
            if (!BeginMemoryMaintenance()) return;
            memoryStatusLabel.Text = progressText;
            memoryStatusLabel.ForeColor = Theme.Warning;
            try
            {
                int result = await Task.Run(action);
                memoryStatusLabel.Text = resultPrefix + ": " + NativeMemoryResultText(result);
                memoryStatusLabel.ForeColor = result == 0 ? Theme.Good : Theme.Danger;
                RefreshMemoryPage();
            }
            catch (Exception ex)
            {
                memoryStatusLabel.Text = resultPrefix + " failed: " + ex.Message;
                memoryStatusLabel.ForeColor = Theme.Danger;
            }
            finally
            {
                EndMemoryMaintenance();
            }
        }

        private async Task BlockSelectedAsync(bool block)
        {
            string path = SelectedNetworkPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "The selected connection has no executable path. Some protected processes hide path data; try Restart as Admin and refresh.", "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (block && MessageBox.Show(this, "Block outbound network access for:\n" + path, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!BeginFirewallAction()) return;
            try
            {
                if (!await TryApplyFirewallRuleAsync(path, block)) return;

                firewallStatusCache[path] = block ? FirewallStatusBlocked : FirewallStatusNoBlock;
                firewallStateRevision++;
                MessageBox.Show(this, block
                    ? "Blocked outbound network access for this app."
                    : "Removed this app's Better Task Manager block rule.",
                    "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                EndFirewallAction();
            }
        }

        private async Task<bool> TryApplyFirewallRuleAsync(string path, bool block)
        {
            CommandResult result;
            if (isAdmin)
            {
                result = await Task.Run(() => RunFirewallRuleCommand(path, block));
            }
            else
            {
                try
                {
                    result = await RunElevatedFirewallRuleCommandAsync(path, block);
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    MessageBox.Show(this, "Administrator approval was cancelled. The firewall was not changed.",
                        "Better Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }

            if (result.Succeeded) return true;
            ShowCommandFailure(block ? "Blocking outbound network access" : "Removing the firewall rule", result);
            return false;
        }

        private static async Task<CommandResult> RunElevatedFirewallRuleCommandAsync(string path, bool block)
        {
            ProcessStartInfo startInfo = CreateFirewallHelperStartInfo(path, block);

            Process process = Process.Start(startInfo);
            if (process == null) return new CommandResult(-1, "", "Windows did not start the elevated firewall helper.", false);
            using (process)
            {
                int exitCode = await Task.Run(() =>
                {
                    process.WaitForExit();
                    return process.ExitCode;
                });
                return new CommandResult(exitCode, "", exitCode == 0 ? "" : "The elevated firewall helper returned exit code " + exitCode.ToString(CultureInfo.InvariantCulture) + ".", false);
            }
        }

        internal static ProcessStartInfo CreateFirewallHelperStartInfo(string path, bool block)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(block ? "--firewall-block" : "--firewall-unblock");
            startInfo.ArgumentList.Add(path ?? "");
            return startInfo;
        }

        internal static CommandResult RunFirewallRuleCommand(string path, bool block)
        {
            if (string.IsNullOrWhiteSpace(path)) return new CommandResult(87, "", "The executable path is empty.", false);
            string rule = RuleNameForPath(path);
            return block
                ? CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "add", "rule", "name=" + rule, "dir=out", "program=" + path, "action=block", "profile=any")
                : CommandRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", "name=" + rule);
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
                historyNoteLabel.Text = historyRecordingEnabled
                    ? "Connection history cleared. Live monitoring can record new changes."
                    : "Connection history cleared. Recording remains off.";
                historyNoteLabel.ForeColor = historyRecordingEnabled ? Theme.Good : Theme.Warning;
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
                if (!historyRecordingEnabled)
                {
                    latestHistoryRows = await Task.Run(() => historyStore.LoadRecent(2000));
                    FillHistoryGrid(false);
                    MarkLiveRefreshSuccess();
                    return;
                }
                var result = await RunSnapshotCollectionAsync(historyTab, () =>
                {
                    var issues = new List<string>();
                    List<NetworkRow> connections = BuildNetworkRows(null, issues);
                    DateTime sampledAt = connections.Count > 0 ? connections[0].Timestamp : DateTime.Now;
                    int recorded = historyStore.SaveSnapshot(connections, sampledAt);
                    List<string[]> history = historyStore.LoadRecent(2000);
                    return Tuple.Create(history, connections.Count, recorded, sampledAt, issues);
                });
                if (result == null) return;

                latestHistoryRows = result.Item1;
                latestNetworkIssues = result.Item5;
                FillHistoryGrid(false);
                historyNoteLabel.Text = "Live " + result.Item4.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + ": " +
                    result.Item2.ToString(CultureInfo.CurrentCulture) + " active, " +
                    result.Item3.ToString(CultureInfo.CurrentCulture) + " recorded. " + historyNoteLabel.Text + NetworkIssueSummary(result.Item5);
                if (result.Item5.Count > 0) historyNoteLabel.ForeColor = Theme.Warning;
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
            if (!historyRecordingEnabled)
            {
                historyNoteLabel.Text = "Recording off. " + historyNoteLabel.Text;
                historyNoteLabel.ForeColor = Theme.Warning;
            }
        }

        private void UpdateHistoryRecordingStatus()
        {
            FillHistoryGrid(false);
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
                string value = index < row.Length ? row[index] : "";
                item.SubItems.Add(index == 9 ? UiText.Translate(value) : value);
            }
            e.Item = item;
        }

        internal static bool HistoryRowMatchesFilter(string[] row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (row == null) return false;
            return row.Any(value => (value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                UiText.Translate(value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal async Task RunUiSmokeTestAsync()
        {
            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                throw new InvalidOperationException("WinForms did not start in PerMonitorV2 high-DPI mode.");
            }
            if (Theme.Window != Color.FromArgb(27, 22, 41) || Theme.Accent != Color.FromArgb(139, 92, 246) ||
                Theme.AccentHover != Color.FromArgb(167, 139, 250) || Theme.AccentSelected != Color.FromArgb(109, 75, 171))
            {
                throw new InvalidOperationException("Violet-slate theme colors regressed.");
            }
            if (UiText.IsGerman &&
                (!navBar.Controls.OfType<Button>().Any(button => button.Text == "Anwendungen") ||
                 appSearchBox.PlaceholderText != "Anwendungen suchen" ||
                 processGrid.Columns["PeakWorkingSetMB"].HeaderText != "Max. Arbeitssatz (MB)" ||
                 Convert.ToString(refreshIntervalBox.Items[2], CultureInfo.InvariantCulture) != "5 Sek." ||
                 appConnectionCard.Text.IndexOf("Gruppenverbindungen", StringComparison.Ordinal) < 0 ||
                 memoryCpuTrend.Title.IndexOf("letzte 60 Messwerte", StringComparison.Ordinal) < 0))
            {
                throw new InvalidOperationException("German control, header, interval, card, or trend localization regressed.");
            }
            string[] appContextActions = appGrid.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            string[] processContextActions = processGrid.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            string[] networkContextActions = networkGrid.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            string[] historyContextActions = historyList.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            string[] memoryContextActions = memoryPanel.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
            if (sectionContextMenus.Count != 5 ||
                !appContextActions.SequenceEqual(new[] { "Refresh Apps", "Export CSV", "Block App", "Unblock App", "View Processes", "Open Folder", "Copy Path" }.Select(UiText.Translate)) ||
                !processContextActions.SequenceEqual(new[] { "Refresh", "Force Kill", "Export CSV", "Open Folder", "Copy Path" }.Select(UiText.Translate)) ||
                !networkContextActions.SequenceEqual(new[] { "Refresh", "Block App", "Unblock App", "Export CSV", "Open Folder", "Copy Path" }.Select(UiText.Translate)) ||
                !historyContextActions.SequenceEqual(new[] { "Refresh", "Export CSV", "Clear History", "Previous", "Next", "Record history" }.Select(UiText.Translate)) ||
                !memoryContextActions.SequenceEqual(new[] { "Refresh", "Trim App Memory", "Clear Standby Cache", "Release System Cache" }.Select(UiText.Translate)) ||
                appGrid.ContextMenuStrip == processGrid.ContextMenuStrip || processGrid.ContextMenuStrip == networkGrid.ContextMenuStrip ||
                appSearchBox.ContextMenuStrip != null)
            {
                throw new InvalidOperationException("Page-specific right-click action menus were not configured correctly.");
            }
            if (processGrid.Columns["PeakWorkingSetMB"].Width < 175 || processGrid.Columns["PeakWorkingSetMB"].MinimumWidth < 175 ||
                appGrid.Columns["Firewall"].Width < 150 || appGrid.Columns["Firewall"].MinimumWidth < 150 ||
                processToolbar.Controls.OfType<Button>().Count() != 5 ||
                processToolbar.Controls.OfType<Button>().Count(button => button.Text == UiText.Translate("Refresh")) != 1)
            {
                throw new InvalidOperationException("Process toolbar consolidation or Peak Working Set visibility regressed.");
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
            if (!killButton.Enabled || !BeginProcessAction() || killButton.Enabled ||
                processCopyPathButton.Enabled || processOpenFolderButton.Enabled || BeginProcessAction())
            {
                throw new InvalidOperationException("Process mutation gate did not enter a single busy state.");
            }
            EndProcessAction();
            if (!killButton.Enabled || !processCopyPathButton.Enabled || !processOpenFolderButton.Enabled)
            {
                throw new InvalidOperationException("Process actions did not recover after leaving the busy state.");
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
            gridSortState[networkGrid] = Tuple.Create("RemotePort", true);
            RefreshSortIndicator(networkGrid);
            if (networkGrid.Columns["RemotePort"].HeaderText != UiText.Translate("Remote Port") ||
                CurrentSortOrder(networkGrid, "RemotePort") != SortOrder.Ascending ||
                CurrentSortOrder(networkGrid, "PID") != SortOrder.None ||
                networkGrid.Columns["RemotePort"].HeaderCell.SortGlyphDirection != SortOrder.None)
            {
                throw new InvalidOperationException("Network Remote Port did not expose the high-contrast custom sort indicator.");
            }
            gridSortState[networkGrid] = Tuple.Create("RemotePort", false);
            RefreshSortIndicator(networkGrid);
            if (networkGrid.Columns["RemotePort"].HeaderText != UiText.Translate("Remote Port") ||
                CurrentSortOrder(networkGrid, "RemotePort") != SortOrder.Descending)
            {
                throw new InvalidOperationException("Network Remote Port descending sort indicator did not update.");
            }
            gridSortState[networkGrid] = Tuple.Create("PID", false);
            RefreshSortIndicator(networkGrid);
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
            latestNetworkIssues = new List<string> { "IPv6 UDP: expected partial source" };
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
            if (appMetaLabel.Text.IndexOf(UiText.Translate("Network data partial:"), StringComparison.Ordinal) < 0 || appMetaLabel.ForeColor != Theme.Warning)
            {
                throw new InvalidOperationException("Apps did not disclose partial native network data.");
            }
            if (appFirewallCard.Text.IndexOf(UiText.Translate("Not blocked by BTM"), StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Selected-app firewall card did not follow the active UI language.");
            }
            if (!appBlockButton.Enabled || appUnblockButton.Enabled || !BeginFirewallAction() || appBlockButton.Enabled || appUnblockButton.Enabled ||
                blockButton.Enabled || unblockButton.Enabled || BeginFirewallAction())
            {
                throw new InvalidOperationException("Firewall mutation gate did not enter a single cross-view busy state.");
            }
            EndFirewallAction();
            if (!appBlockButton.Enabled || appUnblockButton.Enabled || !blockButton.Enabled || !unblockButton.Enabled)
            {
                throw new InvalidOperationException("Firewall action controls did not recover for standard-user just-in-time elevation.");
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
            if (memoryCpuTrend.SampleCount != 0 || memoryLoadTrend.SampleCount != 1)
            {
                throw new InvalidOperationException("Memory trends did not respect initial CPU/RAM sample availability.");
            }
            using (var boundedTrend = new PercentageTrendControl(3))
            {
                boundedTrend.AddSample(-5);
                boundedTrend.AddSample(50);
                boundedTrend.AddSample(150);
                boundedTrend.AddSample(75);
                double[] samples = boundedTrend.SnapshotSamples();
                if (samples.Length != 3 || samples[0] != 50 || samples[1] != 100 || samples[2] != 75)
                {
                    throw new InvalidOperationException("Percentage trend did not clamp and bound its sample history.");
                }
            }
            if (!BeginMemoryMaintenance() || trimAllButton.Enabled || clearStandbyButton.Enabled || emptySystemButton.Enabled || BeginMemoryMaintenance())
            {
                throw new InvalidOperationException("Memory maintenance gate did not enter a single busy state.");
            }
            EndMemoryMaintenance();
            if (!trimAllButton.Enabled || clearStandbyButton.Enabled != hasSystemMemoryPrivilege || emptySystemButton.Enabled != hasSystemMemoryPrivilege)
            {
                throw new InvalidOperationException("Memory maintenance controls did not return to their idle privilege state.");
            }
            VerifyNarrowLayout();
            if (!await HandleGlobalShortcutAsync(Keys.Control | Keys.D5) || activePage != memoryTab)
            {
                throw new InvalidOperationException("Ctrl+5 did not navigate to the Memory page.");
            }
            if (memoryLoadTrend.SampleCount != 2)
            {
                throw new InvalidOperationException("Memory navigation did not append the next RAM trend sample.");
            }
            ((ToolStripMenuItem)memoryPanel.ContextMenuStrip.Items[0]).PerformClick();
            await Task.Yield();
            if (activePage != memoryTab || memoryLoadTrend.SampleCount != 3)
            {
                throw new InvalidOperationException("Memory right-click Refresh did not forward to the section action.");
            }
            int historyCountBeforeOptOut = historyStore.LoadRecent(2000).Count;
            historyRecordingCheck.Checked = false;
            SaveNetworkHistory(new List<NetworkRow>
            {
                new NetworkRow { Timestamp = DateTime.Now, Process = "opt-out-probe", Pid = 9999, Protocol = "TCP", LocalAddress = "127.0.0.1", LocalPort = "1", RemoteAddress = "127.0.0.1", RemotePort = "2", State = "Established", Path = "C:\\opt-out.exe" }
            });
            if (historyRecordingEnabled || historyStore.LoadRecent(2000).Count != historyCountBeforeOptOut ||
                historyNoteLabel.Text.IndexOf(UiText.Translate("Recording off."), StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("History recording opt-out did not stop writes and disclose state.");
            }
            refreshIntervalBox.SelectedIndex = 3;
            networkGrid.Columns["Process"].Width = 222;
            SaveAppSettings();
            AppSettings savedUiSettings = settingsStore.Load();
            int savedNetworkWidth;
            if (savedUiSettings.RefreshIntervalIndex != 3 || savedUiSettings.RecordHistory || savedUiSettings.ColumnWidths == null ||
                !savedUiSettings.ColumnWidths.TryGetValue("Network.Process", out savedNetworkWidth) || savedNetworkWidth != 222)
            {
                throw new InvalidOperationException("Main window did not persist Live interval and column width preferences.");
            }
        }

        internal async Task RunUiSoakTestAsync()
        {
            const int rounds = 3;
            const int maximumRefreshMilliseconds = 8000;
            for (int round = 1; round <= rounds; round++)
            {
                await VerifySoakRefreshAsync("Apps", appsTab, () => RefreshAppsAsync(false, true),
                    () => !refreshingApps && appRefreshButton.Enabled, maximumRefreshMilliseconds, round);
                await VerifySoakRefreshAsync("Processes", processTab, () => RefreshProcessesAsync(true),
                    () => !refreshingProcesses && refreshButton.Enabled, maximumRefreshMilliseconds, round);
                await VerifySoakRefreshAsync("Network", networkTab, () => RefreshNetworkAsync(true),
                    () => !refreshingNetwork && networkRefreshButton.Enabled, maximumRefreshMilliseconds, round);
                await VerifySoakRefreshAsync("History", historyTab, () => RefreshHistoryLiveAsync(),
                    () => !loadingHistory && !refreshingHistory && reloadHistoryButton.Enabled && clearHistoryButton.Enabled,
                    maximumRefreshMilliseconds, round);
                await VerifySoakRefreshAsync("Memory", memoryTab, () =>
                {
                    if (!RefreshMemoryPage()) throw new InvalidOperationException("Memory refresh returned a failure state.");
                    return Task.CompletedTask;
                }, () => true, maximumRefreshMilliseconds, round);
            }

            if (snapshotCollectionGate.CurrentCount != 1)
            {
                throw new InvalidOperationException("Snapshot collection gate was not released after the UI soak test.");
            }
        }

        private async Task VerifySoakRefreshAsync(string pageName, Control page, Func<Task> refresh, Func<bool> idleState,
            int maximumRefreshMilliseconds, int round)
        {
            ShowPage(page);
            var stopwatch = Stopwatch.StartNew();
            await refresh();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > maximumRefreshMilliseconds)
            {
                throw new InvalidOperationException(pageName + " refresh exceeded " + maximumRefreshMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " ms during soak round " + round.ToString(CultureInfo.InvariantCulture) + ".");
            }
            if (!idleState())
            {
                throw new InvalidOperationException(pageName + " did not return to its idle action state during soak round " +
                    round.ToString(CultureInfo.InvariantCulture) + ".");
            }
            var messagePumpProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke(new Action(() => messagePumpProbe.TrySetResult(true)));
            Task completed = await Task.WhenAny(messagePumpProbe.Task, Task.Delay(1000));
            if (completed != messagePumpProbe.Task || !messagePumpProbe.Task.Result)
            {
                throw new InvalidOperationException("The UI message pump did not recover after " + pageName + " soak refresh round " +
                    round.ToString(CultureInfo.InvariantCulture) + ".");
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
            VerifyAppsDetailAlignment();

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

        private void VerifyAppsDetailAlignment()
        {
            int[] leftEdges = new Control[] { appTitleLabel, appMetaLabel, appMetricCards, appActions, appFirewallDetailsLabel, appConnectionsGrid }
                .Select(control => control.PointToScreen(Point.Empty).X)
                .ToArray();
            if (leftEdges.Max() - leftEdges.Min() > 1)
            {
                throw new InvalidOperationException("Apps detail heading, metadata, cards, actions, status, and connection grid do not share one left edge.");
            }
            if (appFirewallDetailsLabel.Parent == appActions)
            {
                throw new InvalidOperationException("Apps firewall status must use its own aligned row instead of wrapping inside the action bar.");
            }
            if (!(appTitleLabel is TightLabel) || !(appMetaLabel is TightLabel) || !(appFirewallDetailsLabel is TightLabel))
            {
                throw new InvalidOperationException("Apps aligned text must use tight rendering without glyph-overhang padding.");
            }
            if (appSearchBox.Dock != DockStyle.None || appSearchBox.Anchor != (AnchorStyles.Left | AnchorStyles.Right) ||
                !appSearchBox.Multiline || appSearchBox.Height != 30 || appSearchBox.TextTopOffset < 3)
            {
                throw new InvalidOperationException("Apps search must use an explicit vertically centered edit formatting rectangle.");
            }
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
            ProcessRow selectedProcess = SelectedProcessRow();
            string processPath = SelectedGridPath(processGrid);
            bool processActionsAvailable = !refreshingProcesses && !refreshingProcessDetails && !processActionInProgress && selectedProcess != null;
            killButton.Enabled = processActionsAvailable && CanForceKillProcess(selectedProcess == null ? 0 : selectedProcess.Pid, Environment.ProcessId);
            processCopyPathButton.Enabled = processActionsAvailable && !string.IsNullOrWhiteSpace(processPath);
            processOpenFolderButton.Enabled = processActionsAvailable && !string.IsNullOrWhiteSpace(ExecutableDirectory(processPath));

            string networkPath = SelectedGridPath(networkGrid);
            networkCopyPathButton.Enabled = !refreshingNetwork && !string.IsNullOrWhiteSpace(networkPath);
            networkOpenFolderButton.Enabled = !refreshingNetwork && !string.IsNullOrWhiteSpace(ExecutableDirectory(networkPath));
        }

        private void UpdateFirewallActionButtons()
        {
            AppProfile app = SelectedAppProfile();
            string appPath = app == null ? "" : app.Path ?? "";
            string appStatus = GetFirewallStatus(appPath);
            bool appEligible = !firewallActionInProgress && !refreshingApps && !string.IsNullOrWhiteSpace(appPath);
            appBlockButton.Enabled = appEligible && appStatus != FirewallStatusBlocked;
            appUnblockButton.Enabled = appEligible && appStatus == FirewallStatusBlocked;

            string networkPath = SelectedGridPath(networkGrid);
            bool networkEligible = !firewallActionInProgress && !refreshingNetwork && !string.IsNullOrWhiteSpace(networkPath);
            blockButton.Enabled = networkEligible;
            unblockButton.Enabled = networkEligible;
        }

        private bool BeginFirewallAction()
        {
            if (firewallActionInProgress) return false;
            firewallActionInProgress = true;
            UpdateFirewallActionButtons();
            return true;
        }

        private void EndFirewallAction()
        {
            firewallActionInProgress = false;
            UpdateFirewallActionButtons();
        }

        private bool BeginProcessAction()
        {
            if (processActionInProgress || refreshingProcesses || refreshingProcessDetails) return false;
            processActionInProgress = true;
            UpdateExecutablePathActions();
            return true;
        }

        private void EndProcessAction()
        {
            processActionInProgress = false;
            UpdateExecutablePathActions();
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
                Title = UiText.Translate(title),
                Filter = UiText.Translate("CSV files (*.csv)|*.csv|All files (*.*)|*.*"),
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
            if (!historyRecordingEnabled) return;
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
            if (unsigned == 0xC0000061) return "Failed: Windows did not grant SeProfileSingleProcessPrivilege to this process.";
            if (unsigned == 0xC0000005) return "Failed: Windows denied access.";
            return "Failed: Windows returned native status 0x" + unsigned.ToString("X8", CultureInfo.InvariantCulture) + ".";
        }

        private string SystemMemoryPrivilegeUnavailableText()
        {
            if (!isAdmin) return "System-memory actions require Restart as Admin and SeProfileSingleProcessPrivilege.";
            if (systemMemoryPrivilegeError == 1300) return "This elevated token does not contain SeProfileSingleProcessPrivilege; the two system-memory actions are unavailable under the current Windows policy.";
            return "Windows could not enable SeProfileSingleProcessPrivilege (error " + systemMemoryPrivilegeError.ToString(CultureInfo.InvariantCulture) + "); the two system-memory actions are unavailable.";
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

        internal static string RuleNameForPath(string path)
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
            ApplyLanguageOverride(args);
            bool firewallHelperRequested = args != null && args.Any(argument =>
                string.Equals(argument, "--firewall-block", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--firewall-unblock", StringComparison.OrdinalIgnoreCase));
            if (firewallHelperRequested)
            {
                Environment.ExitCode = RunFirewallHelper(args);
                return;
            }

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

            bool runUiSoak = args != null && args.Any(a => string.Equals(a, "--ui-soak-test", StringComparison.OrdinalIgnoreCase));
            if (runUiSoak || (args != null && args.Any(a =>
                string.Equals(a, "--ui-smoke-test", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--history-ui-smoke-test", StringComparison.OrdinalIgnoreCase))))
            {
                RunUiSmokeTest(runUiSoak);
                return;
            }

            Run();
        }

        internal static void ApplyLanguageOverride(string[] args)
        {
            string argument = (args ?? Array.Empty<string>()).FirstOrDefault(value =>
                value != null && (value.StartsWith("--language=", StringComparison.OrdinalIgnoreCase) ||
                                  value.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)));
            if (string.IsNullOrWhiteSpace(argument)) return;
            int separator = argument.IndexOf('=');
            string language = separator < 0 ? "" : argument.Substring(separator + 1).Trim();
            string cultureName;
            if (string.Equals(language, "de", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "de-DE", StringComparison.OrdinalIgnoreCase)) cultureName = "de-DE";
            else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)) cultureName = "en-US";
            else return;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        internal static bool TryParseFirewallHelperRequest(string[] args, out bool block, out string path)
        {
            block = false;
            path = "";
            if (args == null || args.Length != 2) return false;
            if (string.Equals(args[0], "--firewall-block", StringComparison.OrdinalIgnoreCase)) block = true;
            else if (!string.Equals(args[0], "--firewall-unblock", StringComparison.OrdinalIgnoreCase)) return false;
            path = args[1] ?? "";
            return !string.IsNullOrWhiteSpace(path);
        }

        private static int RunFirewallHelper(string[] args)
        {
            bool block;
            string path;
            if (!TryParseFirewallHelperRequest(args, out block, out path)) return 87;
            bool administrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (!administrator) return 5;
            CommandResult result = MainForm.RunFirewallRuleCommand(path, block);
            if (result.Succeeded) return 0;
            if (result.TimedOut) return 1460;
            return result.ExitCode == 0 ? 1 : result.ExitCode;
        }

        private static void RunUiSmokeTest(bool runSoak)
        {
            ConfigureApplicationVisuals();

            int completed = 0;
            string temporaryFolder = Path.Combine(Path.GetTempPath(), "BetterTaskManager-HistoryUiTest-" + Guid.NewGuid().ToString("N"));
            var form = new MainForm(true,
                Path.Combine(temporaryFolder, "network-history.csv"),
                Path.Combine(temporaryFolder, "settings.json"));
            Task.Run(async () =>
            {
                await Task.Delay(runSoak ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(10));
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
                    if (runSoak) await form.RunUiSoakTestAsync();
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
                CrashLogWriter.Append(Path.Combine(folder, "crash.log"), BuildCrashReport(ex, DateTime.Now));
            }
            catch { }
        }

        internal static string BuildCrashReport(Exception ex, DateTime timestamp)
        {
            var text = new StringBuilder();
            text.AppendLine("Timestamp: " + timestamp.ToString("O", CultureInfo.InvariantCulture));
            text.AppendLine("Version: " + Application.ProductVersion);
            text.AppendLine("Runtime: " + RuntimeInformation.FrameworkDescription);
            text.AppendLine("OS: " + RuntimeInformation.OSDescription);
            text.AppendLine("Process: " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            text.AppendLine("High DPI: " + Application.HighDpiMode);
            text.AppendLine();
            text.AppendLine(ex == null ? "Unknown exception" : ex.ToString());
            text.AppendLine();
            return text.ToString();
        }

        public static string SelfTest()
        {
            if (UiText.TranslateToGerman("Apps") != "Anwendungen" ||
                UiText.TranslateToGerman("Snapshot 12:34    3 processes aggregated    CPU sampling...").IndexOf("Momentaufnahme 12:34", StringComparison.Ordinal) < 0 ||
                UiText.TranslateToGerman("Snapshot 12:34    3 processes aggregated    CPU sampling...").IndexOf("3 Prozesse zusammengefasst", StringComparison.Ordinal) < 0 ||
                UiText.TranslateToGerman("Not blocked by BTM") != "Nicht durch BTM blockiert" ||
                UiText.TranslateToGerman("Remote Port") != "Remoteport")
            {
                throw new InvalidOperationException("German localization mapping failed.");
            }
            CommandResult success = CommandRunner.Run("cmd.exe", "/d", "/c", "echo better-task-manager-self-test");
            if (!success.Succeeded) throw new InvalidOperationException("Command runner success probe failed. " + success.FailureSummary());
            if (success.StandardOutput.IndexOf("better-task-manager-self-test", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Command runner did not capture standard output.");

            CommandResult failure = CommandRunner.Run("cmd.exe", "/d", "/c", "echo expected-failure 1>&2 & exit 7");
            if (failure.Succeeded || failure.ExitCode != 7) throw new InvalidOperationException("Command runner failure probe did not preserve exit code 7.");
            if (failure.StandardError.IndexOf("expected-failure", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Command runner did not capture standard error.");

            bool helperBlock;
            string helperPath;
            ProcessStartInfo helperStartInfo = MainForm.CreateFirewallHelperStartInfo("C:\\Program Files\\Test App\\app.exe", true);
            if (!TryParseFirewallHelperRequest(new[] { "--firewall-block", "C:\\Program Files\\Test App\\app.exe" }, out helperBlock, out helperPath) ||
                !helperBlock || helperPath != "C:\\Program Files\\Test App\\app.exe" ||
                !TryParseFirewallHelperRequest(new[] { "--firewall-unblock", "C:\\Apps\\app.exe" }, out helperBlock, out helperPath) || helperBlock ||
                TryParseFirewallHelperRequest(new[] { "--firewall-block" }, out helperBlock, out helperPath) ||
                !helperStartInfo.UseShellExecute || helperStartInfo.Verb != "runas" || helperStartInfo.ArgumentList.Count != 2 ||
                helperStartInfo.ArgumentList[0] != "--firewall-block" || helperStartInfo.ArgumentList[1] != "C:\\Program Files\\Test App\\app.exe" ||
                MainForm.RuleNameForPath("C:\\Apps\\APP.exe") != MainForm.RuleNameForPath("c:\\apps\\app.exe"))
            {
                throw new InvalidOperationException("Elevated firewall helper parsing or deterministic rule naming failed.");
            }

            int enabledPrivilegeError;
            if (!NativeMethods.TryEnablePrivilege("SeChangeNotifyPrivilege", out enabledPrivilegeError))
            {
                throw new InvalidOperationException("Token privilege activation probe failed with Windows error " + enabledPrivilegeError.ToString(CultureInfo.InvariantCulture) + ".");
            }
            int missingPrivilegeError;
            if (NativeMethods.TryEnablePrivilege("BetterTaskManagerPrivilegeThatDoesNotExist", out missingPrivilegeError) || missingPrivilegeError == 0)
            {
                throw new InvalidOperationException("Token privilege activation did not reject an unknown privilege name.");
            }

            NativeNetworkSnapshot nativeNetworkSnapshot = NativeNetworkCollector.GetSnapshot();
            List<NativeConnection> connections = nativeNetworkSnapshot.Connections;
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
            var partialNetworkSnapshot = new NativeNetworkSnapshot();
            NativeNetworkCollector.AddSourceResult(partialNetworkSnapshot, "working", () => new List<NativeConnection> { new NativeConnection { Protocol = "TCP" } });
            NativeNetworkCollector.AddSourceResult(partialNetworkSnapshot, "failed", () => throw new InvalidOperationException("expected table failure"));
            bool lowerBufferRejected = false;
            bool upperBufferRejected = false;
            try { NativeNetworkCollector.ValidateBufferSize(3, "test"); } catch (InvalidDataException) { lowerBufferRejected = true; }
            try { NativeNetworkCollector.ValidateBufferSize((64 * 1024 * 1024) + 1, "test"); } catch (InvalidDataException) { upperBufferRejected = true; }
            string issueSummary = MainForm.NetworkIssueSummary(new[] { "one", "two", "three" });
            string appIssueSummary = MainForm.AppNetworkCompletenessText(new[] { "one", "two" });
            if (partialNetworkSnapshot.Connections.Count != 1 || partialNetworkSnapshot.Issues.Count != 1 ||
                partialNetworkSnapshot.Issues[0].IndexOf("expected table failure", StringComparison.Ordinal) < 0 ||
                !lowerBufferRejected || !upperBufferRejected || issueSummary.IndexOf("+1", StringComparison.Ordinal) < 0 ||
                appIssueSummary.IndexOf("2 native table warnings", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Partial native network collection or buffer validation failed.");
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
            if (PercentageTrendControl.NormalizePercentage(double.NaN) != 0 ||
                PercentageTrendControl.NormalizePercentage(-1) != 0 ||
                PercentageTrendControl.NormalizePercentage(101) != 100 ||
                PercentageTrendControl.NormalizePercentage(42.5) != 42.5)
            {
                throw new InvalidOperationException("Percentage trend normalization failed.");
            }

            TestHistoryStore();
            TestSettingsStore();
            TestCrashLogWriter();
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
            if (!MainForm.ProcessStartTimesMatch(100, 100) || MainForm.ProcessStartTimesMatch(100, 200) ||
                !MainForm.ProcessStartTimesMatch(0, 200) || MainForm.CanForceKillProcess(4242, 4242) || !MainForm.CanForceKillProcess(4242, 7) ||
                MainForm.ShouldTrimProcess(4242, 4242) || MainForm.ShouldTrimProcess(0, 4242) || !MainForm.ShouldTrimProcess(7, 4242))
            {
                throw new InvalidOperationException("Destructive process identity safety policy failed.");
            }
            var trimSummaryProbe = new MemoryTrimResult { Trimmed = 12, Inaccessible = 3, Exited = 2, OtherFailed = 1, Skipped = 2 };
            string standardTrimSummary = MainForm.MemoryTrimSummaryText(trimSummaryProbe, false);
            string adminTrimSummary = MainForm.MemoryTrimSummaryText(trimSummaryProbe, true);
            if (MainForm.MemoryTrimOutcomeForWin32Error(5) != MemoryTrimOutcome.Inaccessible ||
                MainForm.MemoryTrimOutcomeForWin32Error(1314) != MemoryTrimOutcome.Inaccessible ||
                MainForm.MemoryTrimOutcomeForWin32Error(6) != MemoryTrimOutcome.Exited ||
                MainForm.MemoryTrimOutcomeForWin32Error(1234) != MemoryTrimOutcome.OtherFailed ||
                standardTrimSummary.IndexOf("Restart as Admin", StringComparison.Ordinal) < 0 ||
                adminTrimSummary.IndexOf("Protected services", StringComparison.Ordinal) < 0 ||
                adminTrimSummary.IndexOf("3 protected/access denied", StringComparison.Ordinal) < 0 ||
                adminTrimSummary.IndexOf("2 exited during scan", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Working-set trim failure categorization or guidance failed.");
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
            DateTime firewallSnapshot = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
            if (!MainForm.ShouldApplyFirewallResult(firewallSnapshot, firewallSnapshot, 7, 7) ||
                MainForm.ShouldApplyFirewallResult(firewallSnapshot.AddSeconds(1), firewallSnapshot, 7, 7) ||
                MainForm.ShouldApplyFirewallResult(firewallSnapshot, firewallSnapshot, 8, 7))
            {
                throw new InvalidOperationException("Late firewall result staleness policy failed.");
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
                if (Application.ProductVersion != "1.1.0-preview.53" || form.Text != "Better Task Manager v1.1.0-preview.53")
                {
                    throw new InvalidOperationException("Application version metadata and window title do not match 1.1.0-preview.53.");
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

                string sharedHistoryPath = Path.Combine(temporaryFolder, "multi-instance-history.csv");
                var firstStore = new NetworkHistoryStore(sharedHistoryPath);
                var secondStore = new NetworkHistoryStore(sharedHistoryPath);
                var secondRow = new NetworkRow
                {
                    Timestamp = row.Timestamp,
                    Process = "Second App",
                    Pid = 4343,
                    User = "TEST\\User",
                    Protocol = "TCP",
                    LocalAddress = "127.0.0.1",
                    LocalPort = "23456",
                    RemoteAddress = "127.0.0.1",
                    RemotePort = "8443",
                    State = "Established",
                    Path = "C:\\Apps\\second.exe"
                };
                Task<int> firstWrite = Task.Run(() => firstStore.SaveSnapshot(new[] { row }, row.Timestamp));
                Task<int> secondWrite = Task.Run(() => secondStore.SaveSnapshot(new[] { secondRow }, secondRow.Timestamp));
                Task.WaitAll(firstWrite, secondWrite);
                if (firstWrite.Result != 1 || secondWrite.Result != 1 || firstStore.LoadRecent(100).Count != 2)
                {
                    throw new InvalidOperationException("Independent History stores did not serialize writes to the same file.");
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
                    RecordHistory = false,
                    ColumnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Network.Process"] = 222 }
                });
                AppSettings loaded = store.Load();
                int networkWidth;
                if (loaded.WindowWidth != 1280 || loaded.WindowHeight != 720 || !loaded.Maximized || loaded.RefreshIntervalIndex != 3 || loaded.RecordHistory ||
                    loaded.ColumnWidths == null || !loaded.ColumnWidths.TryGetValue("Network.Process", out networkWidth) || networkWidth != 222)
                {
                    throw new InvalidOperationException("App settings round-trip failed.");
                }

                File.WriteAllText(settingsPath, "{not valid json", Encoding.UTF8);
                AppSettings fallback = store.Load();
                if (fallback.WindowWidth != 1560 || fallback.WindowHeight != 900 || fallback.Maximized || fallback.RefreshIntervalIndex != 2 || !fallback.RecordHistory || fallback.ColumnWidths == null)
                {
                    throw new InvalidOperationException("Corrupt settings did not fall back to defaults.");
                }
            }
            finally
            {
                try { if (Directory.Exists(temporaryFolder)) Directory.Delete(temporaryFolder, true); } catch { }
            }
        }

        private static void TestCrashLogWriter()
        {
            string temporaryFolder = Path.Combine(Path.GetTempPath(), "BetterTaskManager-CrashLogTest-" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(temporaryFolder, "crash.log");
            string previousPath = Path.Combine(temporaryFolder, "crash.previous.log");
            try
            {
                CrashLogWriter.Append(logPath, new string('A', 180) + Environment.NewLine, 256);
                CrashLogWriter.Append(logPath, new string('B', 180) + Environment.NewLine, 256);
                if (!File.Exists(previousPath) || new FileInfo(previousPath).Length > 256 || new FileInfo(logPath).Length > 256)
                {
                    throw new InvalidOperationException("Crash log rotation did not preserve bounded current/previous files.");
                }

                CrashLogWriter.Append(logPath, new string('C', 2000), 256);
                string bounded = File.ReadAllText(logPath, Encoding.UTF8);
                if (new FileInfo(logPath).Length > 256 || bounded.IndexOf("Crash entry truncated", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("Oversized crash entry was not bounded and marked.");
                }

                string report = BuildCrashReport(new InvalidOperationException("expected crash report"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local));
                if (report.IndexOf("expected crash report", StringComparison.Ordinal) < 0 ||
                    report.IndexOf("Version:", StringComparison.Ordinal) < 0 || report.IndexOf("Runtime:", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("Crash report context is incomplete.");
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
            string[] exportFields = MainForm.AppExportFields(profile, snapshot, "Not blocked by BTM");
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
