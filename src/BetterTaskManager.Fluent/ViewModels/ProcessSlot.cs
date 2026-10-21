using BetterTaskManager.Fluent.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterTaskManager.Fluent.ViewModels;

/// <summary>A reusable row of the Processes table. <see cref="Load"/> only raises changes for values that moved.</summary>
/// <summary>Values shared by every row of one refresh.</summary>
public sealed record ProcessRowContext(long TotalMemory, bool BandwidthAvailable, Func<string, bool> IsBlocked, bool GpuAvailable = true, bool RealDisk = false);

public sealed class ProcessSlot : RowSlot
{
    /// <summary>What UI Automation and screen readers announce for the row.</summary>
    public override string ToString() => Name;

    private static readonly Thickness ChildIndent = new(40, 0, 0, 0);
    private static readonly Thickness NoIndent = new(0);

    private string name = "", detail = "", cpuText = "", memoryText = "", ioText = "", networkText = "", bandwidthText = "", gpuText = "", gpuEngine = "", publisher = "", path = "";
    private ImageSource? icon;
    private Brush cpuHeat = Heat.Level(0), memoryHeat = Heat.Level(0), ioHeat = Heat.Level(0), networkHeat = Heat.Level(0), bandwidthHeat = Heat.Level(0), gpuHeat = Heat.Level(0);
    private Visibility sectionVisibility = Visibility.Collapsed, rowVisibility = Visibility.Visible, chevronVisibility = Visibility.Collapsed;
    private Visibility efficiencyVisibility = Visibility.Collapsed, suspendedVisibility = Visibility.Collapsed;
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
    public string GpuText { get => gpuText; private set => Set(ref gpuText, value); }
    /// <summary>Busiest GPU engine, e.g. "GPU 0 - 3D"; shown as the GPU cell's tooltip.</summary>
    public string? GpuEngine { get => gpuEngine.Length == 0 ? null : gpuEngine; private set => Set(ref gpuEngine, value ?? ""); }
    /// <summary>Bar meters behind the numbers: CPU and GPU over 100 %, memory over an eighth of RAM, disk over 10 MB/s, network over 50 Mbit/s, connections over 50.</summary>
    public Meter Cpu { get; } = new();
    public Meter Memory { get; } = new();
    public Meter Io { get; } = new();
    public Meter Network { get; } = new();
    public Meter Bandwidth { get; } = new();
    public Meter Gpu { get; } = new();
    public Visibility SectionVisibility { get => sectionVisibility; private set => Set(ref sectionVisibility, value); }
    public Visibility RowVisibility { get => rowVisibility; private set => Set(ref rowVisibility, value); }
    public Visibility ChevronVisibility { get => chevronVisibility; private set => Set(ref chevronVisibility, value); }
    public double ChevronAngle { get => chevronAngle; private set => Set(ref chevronAngle, value); }
    public Thickness Indent { get => indent; private set => Set(ref indent, value); }
    public bool Blocked { get => blocked; private set { if (Set(ref blocked, value)) Raise(nameof(BlockedVisibility)); } }
    public Visibility BlockedVisibility => blocked ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EfficiencyVisibility { get => efficiencyVisibility; private set => Set(ref efficiencyVisibility, value); }
    public Visibility SuspendedVisibility { get => suspendedVisibility; private set => Set(ref suspendedVisibility, value); }

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
            slot.Detail = slot.CpuText = slot.MemoryText = slot.IoText = slot.NetworkText = slot.BandwidthText = slot.GpuText = slot.Publisher = slot.Path = "";
            slot.GpuEngine = null;
            slot.ChevronVisibility = Visibility.Collapsed;
            foreach (Meter meter in new[] { slot.Cpu, slot.Memory, slot.Io, slot.Network, slot.Bandwidth, slot.Gpu }) meter.Set(0, 1);
            slot.Blocked = false;
            slot.EfficiencyVisibility = slot.SuspendedVisibility = Visibility.Collapsed;
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
        // Real disk rates are mostly KB/s; one decimal of MB/s would show them all as 0.0.
        slot.IoText = context.RealDisk ? Format.ByteRate(row.Io) : Format.Rate(row.Io);
        slot.NetworkText = row.Connections == 0 ? "–" : Format.Count(row.Connections);
        slot.Cpu.Set(row.Cpu, 100);
        slot.BandwidthText = context.BandwidthAvailable ? Format.Mbps(row.NetworkRate) : "–";
        slot.Bandwidth.Set(context.BandwidthAvailable ? row.NetworkRate * 8 / 1_000_000 : 0, 50);
        slot.GpuText = context.GpuAvailable ? Format.Percent(row.Gpu) : "–";
        slot.GpuEngine = row.GpuEngine;
        slot.Gpu.Set(row.Gpu, 100);
        slot.Memory.Set(row.Memory, context.TotalMemory / 8d);
        slot.Io.Set(row.Io, 10 * 1048576d);
        slot.Network.Set(row.Connections, 50);
        slot.Blocked = row.Kind == RowKind.Group && context.IsBlocked(row.Path);
        slot.EfficiencyVisibility = row.Efficiency ? Visibility.Visible : Visibility.Collapsed;
        slot.SuspendedVisibility = row.Suspended ? Visibility.Visible : Visibility.Collapsed;
    }
}
