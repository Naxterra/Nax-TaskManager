using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

/// <summary>A reusable row of the Processes table. <see cref="Load"/> only raises changes for values that moved.</summary>
/// <summary>Values shared by every row of one refresh.</summary>
public sealed record ProcessRowContext(long TotalMemory, bool BandwidthAvailable, Func<string, bool> IsBlocked);

public sealed class ProcessSlot : ObservableObject
{
    private static readonly Thickness ChildIndent = new(40, 0, 0, 0);
    private static readonly Thickness NoIndent = new(0);

    private string name = "", detail = "", cpuText = "", memoryText = "", ioText = "", networkText = "", bandwidthText = "", publisher = "", path = "";
    private ImageSource? icon;
    private Brush cpuHeat = Heat.Level(0), memoryHeat = Heat.Level(0), ioHeat = Heat.Level(0), networkHeat = Heat.Level(0), bandwidthHeat = Heat.Level(0);
    private Visibility sectionVisibility = Visibility.Collapsed, rowVisibility = Visibility.Visible, chevronVisibility = Visibility.Collapsed;
    private double chevronAngle;
    private Thickness indent;
    private bool blocked;

    public static ColumnLayout? SharedLayout { get; set; }
    public ColumnLayout Layout => SharedLayout!;

    public ProcessRowData? Data { get; private set; }
    public string Key => Data?.Key ?? "";

    public string Name { get => name; private set => Set(ref name, value); }
    public string Detail { get => detail; private set => Set(ref detail, value); }
    public string Publisher { get => publisher; private set => Set(ref publisher, value); }
    public string Path { get => path; private set => Set(ref path, value); }
    public ImageSource? Icon { get => icon; private set => Set(ref icon, value); }
    public string CpuText { get => cpuText; private set => Set(ref cpuText, value); }
    public string MemoryText { get => memoryText; private set => Set(ref memoryText, value); }
    public string IoText { get => ioText; private set => Set(ref ioText, value); }
    public string NetworkText { get => networkText; private set => Set(ref networkText, value); }
    public string BandwidthText { get => bandwidthText; private set => Set(ref bandwidthText, value); }
    public Brush BandwidthHeat { get => bandwidthHeat; private set => Set(ref bandwidthHeat, value); }
    public Brush CpuHeat { get => cpuHeat; private set => Set(ref cpuHeat, value); }
    public Brush MemoryHeat { get => memoryHeat; private set => Set(ref memoryHeat, value); }
    public Brush IoHeat { get => ioHeat; private set => Set(ref ioHeat, value); }
    public Brush NetworkHeat { get => networkHeat; private set => Set(ref networkHeat, value); }
    public Visibility SectionVisibility { get => sectionVisibility; private set => Set(ref sectionVisibility, value); }
    public Visibility RowVisibility { get => rowVisibility; private set => Set(ref rowVisibility, value); }
    public Visibility ChevronVisibility { get => chevronVisibility; private set => Set(ref chevronVisibility, value); }
    public double ChevronAngle { get => chevronAngle; private set => Set(ref chevronAngle, value); }
    public Thickness Indent { get => indent; private set => Set(ref indent, value); }
    public bool Blocked { get => blocked; private set { if (Set(ref blocked, value)) Raise(nameof(BlockedVisibility)); } }
    public Visibility BlockedVisibility => blocked ? Visibility.Visible : Visibility.Collapsed;

    public static void Load(ProcessSlot slot, (ProcessRowData Row, ProcessRowContext Context) input)
    {
        ProcessRowData row = input.Row;
        ProcessRowContext context = input.Context;
        slot.Data = row;
        bool section = row.Kind == RowKind.Section;
        slot.SectionVisibility = section ? Visibility.Visible : Visibility.Collapsed;
        slot.RowVisibility = section ? Visibility.Collapsed : Visibility.Visible;
        slot.Name = row.Name;
        if (section)
        {
            slot.Detail = slot.CpuText = slot.MemoryText = slot.IoText = slot.NetworkText = slot.BandwidthText = slot.Publisher = slot.Path = "";
            slot.ChevronVisibility = Visibility.Collapsed;
            slot.CpuHeat = slot.MemoryHeat = slot.IoHeat = slot.NetworkHeat = slot.BandwidthHeat = Heat.Level(0);
            slot.Blocked = false;
            return;
        }

        slot.Detail = row.Detail;
        slot.Path = row.Path;
        slot.Publisher = row.Publisher;
        slot.Icon = IconCache.Get(row.Path);
        slot.Indent = row.Kind == RowKind.Child ? ChildIndent : NoIndent;
        slot.ChevronVisibility = row.Expandable ? Visibility.Visible : Visibility.Collapsed;
        slot.ChevronAngle = row.Expanded ? 90 : 0;
        slot.CpuText = row.CpuSampled ? Format.Percent(row.Cpu) : "…";
        slot.MemoryText = Format.Memory(row.Memory);
        slot.IoText = Format.Rate(row.Io);
        slot.NetworkText = row.Connections == 0 ? "–" : Format.Count(row.Connections);
        slot.CpuHeat = Heat.Level(Heat.Scale(row.Cpu, 0.5, 2, 5, 12, 25, 50));
        slot.BandwidthText = context.BandwidthAvailable ? Format.Mbps(row.NetworkRate) : "–";
        slot.BandwidthHeat = Heat.Level(context.BandwidthAvailable ? Heat.Scale(row.NetworkRate * 8 / 1_000_000, 0.1, 0.5, 2, 10, 50, 200) : 0);
        double memoryShare = context.TotalMemory == 0 ? 0 : row.Memory * 100d / context.TotalMemory;
        slot.MemoryHeat = Heat.Level(Heat.Scale(memoryShare, 0.2, 0.5, 1, 2, 4, 8));
        slot.IoHeat = Heat.Level(Heat.Scale(row.Io / 1048576d, 0.1, 1, 5, 20, 50, 100));
        slot.NetworkHeat = Heat.Level(Heat.Scale(row.Connections, 1, 5, 15, 30, 60, 120));
        slot.Blocked = row.Kind == RowKind.Group && context.IsBlocked(row.Path);
    }
}
