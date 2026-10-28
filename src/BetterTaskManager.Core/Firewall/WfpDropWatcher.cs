using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using BetterTaskManager.Core.Native;

namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// One connection the Windows Filtering Platform dropped. <see cref="Ours"/> is true when one of this app's filters
/// dropped it; then <see cref="Reason"/> is a <see cref="PolicyReason"/> code (whole-app block, a switch, or
/// "Rule:" with the rule). Otherwise <see cref="Reason"/> is the other filter's name.
/// </summary>
public sealed record BlockedEvent(DateTime TimeUtc, string AppPath, bool Inbound, int Protocol, string RemoteAddress, int RemotePort, int LocalPort,
    bool Ours, string Reason);

public static partial class WfpBlocker
{
    /// <summary>
    /// Subscribes to the filtering engine's classify-drop events. The callback only copies the event into a queue;
    /// <see cref="Drain"/> looks up which filter dropped it and keeps the drops of this app's filters, plus outgoing
    /// drops of programs by other firewalls (inbound drops and drops without a program are mostly network noise).
    /// Needs administrator rights; the History service runs it.
    /// </summary>
    public sealed class DropWatcher : IDisposable
    {
        private const uint NetEventTypeClassifyDrop = 3;
        private const uint DirectionIn = 0x3900;
        private const uint FlagLocalAddrSet = 0x2, FlagRemoteAddrSet = 0x4, FlagLocalPortSet = 0x8, FlagRemotePortSet = 0x10, FlagAppIdSet = 0x20;
        private const int MaxQueued = 5000;

        private readonly ConcurrentQueue<RawDrop> queue = new();
        private readonly Dictionary<ulong, (bool Ours, string Reason, string Path)?> filters = new();
        private readonly NetEventCallback callback;
        private Engine? engine;
        private IntPtr subscription;
        private DateTime filtersReadAt = DateTime.UtcNow;
        private (DateTime Rules, DateTime Blocks) rulesWritten = RulesWritten();
        private int dropped;

        public DropWatcher() => callback = OnEvent;

        /// <summary>Drops lost because the queue was full since the last <see cref="Drain"/>.</summary>
        public int Overflow { get; private set; }

        public void Start()
        {
            engine = Engine.Open();
            // No condition in the template: a net event type condition matched nothing in tests, so the callback
            // checks the type itself.
            IntPtr template = Marshal.AllocHGlobal(Marshal.SizeOf<NetEventEnumTemplate>());
            try
            {
                Marshal.StructureToPtr(new NetEventEnumTemplate { EndTime = long.MaxValue }, template, false);
                var request = new NetEventSubscription { EnumTemplate = template };
                uint result = FwpmNetEventSubscribe4(engine.Handle, ref request, callback, IntPtr.Zero, out subscription);
                if (result != 0) throw new WfpException("FwpmNetEventSubscribe4", result);
            }
            finally
            {
                Marshal.FreeHGlobal(template);
            }
        }

        /// <summary>Runs on a filtering engine thread: copy what is needed and return at once.</summary>
        private void OnEvent(IntPtr context, IntPtr netEvent)
        {
            try
            {
                if (netEvent == IntPtr.Zero || (uint)Marshal.ReadInt32(netEvent, 136) != NetEventTypeClassifyDrop) return;
                IntPtr drop = Marshal.ReadIntPtr(netEvent, 144);
                if (drop == IntPtr.Zero) return;
                if (queue.Count >= MaxQueued)
                {
                    Interlocked.Increment(ref dropped);
                    return;
                }
                uint flags = (uint)Marshal.ReadInt32(netEvent, 8);
                bool v6 = Marshal.ReadInt32(netEvent, 12) == 1;
                string app = "";
                if ((flags & FlagAppIdSet) != 0 && Marshal.ReadInt32(netEvent, 64) is int size and > 2 && Marshal.ReadIntPtr(netEvent, 72) is var data && data != IntPtr.Zero)
                {
                    app = Marshal.PtrToStringUni(data, size / 2 - 1) ?? "";
                }
                queue.Enqueue(new RawDrop(
                    DateTime.FromFileTimeUtc(Marshal.ReadInt64(netEvent, 0)),
                    app,
                    Marshal.ReadByte(netEvent, 16),
                    (flags & FlagLocalAddrSet) != 0 ? Address(netEvent, 20, v6) : "",
                    (flags & FlagRemoteAddrSet) != 0 ? Address(netEvent, 36, v6) : "",
                    (flags & FlagLocalPortSet) != 0 ? (ushort)Marshal.ReadInt16(netEvent, 52) : 0,
                    (flags & FlagRemotePortSet) != 0 ? (ushort)Marshal.ReadInt16(netEvent, 54) : 0,
                    (ulong)Marshal.ReadInt64(drop, 0),
                    (uint)Marshal.ReadInt32(drop, 24) == DirectionIn,
                    Marshal.ReadInt32(drop, 28) != 0));
            }
            catch (Exception)
            {
                // An exception must never cross back into the filtering engine.
            }
        }

        /// <summary>IPv4 addresses come in host byte order, IPv6 addresses as 16 bytes in network order.</summary>
        private static string Address(IntPtr netEvent, int offset, bool v6)
        {
            if (!v6)
            {
                uint value = (uint)Marshal.ReadInt32(netEvent, offset);
                return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }).ToString();
            }
            var bytes = new byte[16];
            Marshal.Copy(netEvent + offset, bytes, 0, 16);
            return new IPAddress(bytes).ToString();
        }

        /// <summary>The drops since the last call that this app's filters caused or that stopped an outgoing connection.</summary>
        public List<BlockedEvent> Drain()
        {
            var events = new List<BlockedEvent>();
            if (engine is null) return events;
            // Filter ids are reused when rules are saved again: forget them as soon as the rule files change, and now
            // and then in case the filters changed without them.
            var written = RulesWritten();
            if (written != rulesWritten || DateTime.UtcNow - filtersReadAt > TimeSpan.FromMinutes(2))
            {
                filters.Clear();
                filtersReadAt = DateTime.UtcNow;
                rulesWritten = written;
            }
            Overflow = Interlocked.Exchange(ref dropped, 0);
            while (queue.TryDequeue(out RawDrop drop))
            {
                if (drop.Loopback && drop.RemoteAddress.Length == 0) continue;
                if (Describe(drop.FilterId) is not { } filter) continue;
                if (!filter.Ours && (drop.Inbound || drop.AppPath.Length == 0)) continue;
                string path = drop.AppPath.Length > 0 ? DevicePaths.ToDosPath(drop.AppPath) : filter.Path;
                events.Add(new BlockedEvent(drop.TimeUtc, path, drop.Inbound, drop.Protocol, drop.RemoteAddress,
                    drop.RemotePort, drop.LocalPort, filter.Ours, filter.Reason));
            }
            return events;
        }

        /// <summary>When the rule file and the blocked-apps list were last written. A missing file gives a fixed early time.</summary>
        private static (DateTime Rules, DateTime Blocks) RulesWritten() =>
            (File.GetLastWriteTimeUtc(NetworkPolicyStore.FilePath), File.GetLastWriteTimeUtc(MirrorPath));

        /// <summary>Which filter a drop came from: ours with its reason code, or another one with its name.</summary>
        private (bool Ours, string Reason, string Path)? Describe(ulong id)
        {
            if (filters.TryGetValue(id, out var known)) return known;
            (bool, string, string)? described = null;
            if (FwpmFilterGetById0(engine!.Handle, id, out IntPtr pointer) == 0 && pointer != IntPtr.Zero)
            {
                try
                {
                    var filter = Marshal.PtrToStructure<Filter>(pointer);
                    string name = Marshal.PtrToStringUni(filter.DisplayData.Name) ?? "";
                    string description = Marshal.PtrToStringUni(filter.DisplayData.Description) ?? "";
                    bool ours = filter.ProviderKey != IntPtr.Zero && Marshal.PtrToStructure<Guid>(filter.ProviderKey) == ProviderKey;
                    if (ours)
                    {
                        // Whole-app blocks are named "Nax-TaskManager Block <hash>" and describe the path; rule
                        // filters are named "Nax-TaskManager <reason>" and describe "rule|<path>".
                        bool rule = description.StartsWith(RuleDescriptionPrefix, StringComparison.Ordinal);
                        string reason = rule && name.StartsWith(PolicyReason.NamePrefix, StringComparison.Ordinal) ? name[PolicyReason.NamePrefix.Length..] : PolicyReason.WholeApp;
                        described = (true, reason, rule ? description[RuleDescriptionPrefix.Length..] : description);
                    }
                    else described = (false, name.Length > 0 ? name : "#" + id, "");
                }
                finally
                {
                    FwpmFreeMemory0(ref pointer);
                }
            }
            filters[id] = described;
            return described;
        }

        public void Dispose()
        {
            if (engine is null) return;
            if (subscription != IntPtr.Zero) FwpmNetEventUnsubscribe0(engine.Handle, subscription);
            subscription = IntPtr.Zero;
            engine.Dispose();
            engine = null;
        }

        private readonly record struct RawDrop(DateTime TimeUtc, string AppPath, int Protocol, string LocalAddress, string RemoteAddress,
            int LocalPort, int RemotePort, ulong FilterId, bool Inbound, bool Loopback);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void NetEventCallback(IntPtr context, IntPtr netEvent);

    /// <summary>FWPM_NET_EVENT_ENUM_TEMPLATE0.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NetEventEnumTemplate
    {
        public long StartTime;
        public long EndTime;
        public uint NumFilterConditions;
        public IntPtr FilterCondition;
    }

    /// <summary>FWPM_NET_EVENT_SUBSCRIPTION0.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NetEventSubscription
    {
        public IntPtr EnumTemplate;
        public uint Flags;
        public Guid SessionKey;
    }

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmNetEventSubscribe4(IntPtr engineHandle, ref NetEventSubscription subscription, NetEventCallback callback, IntPtr context, out IntPtr eventsHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmNetEventUnsubscribe0(IntPtr engineHandle, IntPtr eventsHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterGetById0(IntPtr engineHandle, ulong id, out IntPtr filter);
}
