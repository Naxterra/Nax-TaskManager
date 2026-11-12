using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Network;

internal sealed record AdapterTraffic(string Id, string Name, bool Hardware, bool Filter, bool Up,
    uint Type, uint TunnelType, ulong Received, ulong Sent);

/// <summary>Reads interface counters and the native hardware/filter flags in one snapshot.</summary>
internal static class AdapterTrafficReader
{
    public static unsafe IReadOnlyList<AdapterTraffic> Read()
    {
        uint error = GetIfTable2(out IntPtr table);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            uint count = *(uint*)table;
            var rows = new List<AdapterTraffic>((int)count);
            // MIB_IF_TABLE2 aligns the first MIB_IF_ROW2 to its eight-byte alignment.
            var entry = (InterfaceRow*)((byte*)table + 8);
            for (uint index = 0; index < count; index++)
            {
                InterfaceRow* row = entry + index;
                rows.Add(new AdapterTraffic(row->InterfaceGuid.ToString(), new string(row->Description),
                    (row->Flags & 1) != 0, (row->Flags & 2) != 0, row->OperStatus == 1,
                    row->Type, row->TunnelType, row->InOctets, row->OutOctets));
            }
            return rows;
        }
        finally { FreeMibTable(table); }
    }

    [DllImport("iphlpapi.dll")] private static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);

    // MIB_IF_ROW2, netioapi.h. Keep all fields so native table strides remain correct.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct InterfaceRow
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        public ulong InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos;
        public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors;
        public ulong OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }
}

/// <summary>Counts traffic at one layer: hardware NICs, or non-tunnel NICs on a virtual machine.</summary>
internal sealed class AdapterTrafficAccounting
{
    private Dictionary<string, AdapterTraffic> previous = new();
    private bool? hardwareLayer;

    internal static IReadOnlyList<AdapterTraffic> Select(IReadOnlyList<AdapterTraffic> rows)
    {
        var eligible = rows.Where(row => row.Up && !row.Filter && row.Type is not (24 or 131)
            && row.TunnelType == 0).ToList();
        var hardware = eligible.Where(row => row.Hardware).ToList();
        // A guest may have only a virtual Ethernet NIC. Exclude known VPN adapters in that fallback too.
        return hardware.Count > 0 ? hardware : eligible.Where(row => !VpnTunnels.Matches(row.Name)).ToList();
    }

    public (double Receive, double Send, bool Sampled) Sample(IReadOnlyList<AdapterTraffic> rows, double elapsedSeconds)
    {
        var selected = Select(rows);
        bool hardware = selected.Any(row => row.Hardware);
        var current = selected.ToDictionary(row => row.Id);
        double received = 0, sent = 0;
        int matched = 0;
        if (hardwareLayer == hardware && double.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
        {
            foreach (AdapterTraffic row in selected)
            {
                if (!previous.TryGetValue(row.Id, out var old) || row.Received < old.Received || row.Sent < old.Sent) continue;
                received += row.Received - old.Received;
                sent += row.Sent - old.Sent;
                matched++;
            }
        }
        previous = current;
        hardwareLayer = hardware;
        return matched == 0 ? (0, 0, false) : (received / elapsedSeconds, sent / elapsedSeconds, true);
    }
}
