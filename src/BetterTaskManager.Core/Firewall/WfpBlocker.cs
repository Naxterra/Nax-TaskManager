using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BetterTaskManager.Core.Firewall;

/// <summary>
/// Blocks an executable's outbound connections with persistent Windows Filtering Platform filters, the layer every
/// firewall builds on. Windows Firewall rules (netsh) are not enforced while a third-party firewall such as
/// Bitdefender manages the firewall; WFP filters are. Each blocked path gets one filter per IP version at the
/// ALE connect layer, keyed by a GUID derived from the path, under this app's own provider and sublayer.
/// Changing filters needs administrator rights.
/// </summary>
public static class WfpBlocker
{
    private static readonly Guid ProviderKey = new("6f1b5e6a-1c33-4b8e-9f4c-0a5e2d7c4b11");
    private static readonly Guid SubLayerKey = new("6f1b5e6a-1c33-4b8e-9f4c-0a5e2d7c4b12");
    private static readonly Guid LayerConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid LayerConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid ConditionAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");

    private const uint RpcAuthnWinNt = 10;
    private const uint FlagPersistent = 1;
    private const uint ActionBlock = 0x1001; // FWP_ACTION_BLOCK | FWP_ACTION_FLAG_TERMINATING
    private const uint TypeEmpty = 0, TypeUInt8 = 1, TypeByteBlob = 12;
    private const uint MatchEqual = 0;
    private const uint ErrorAlreadyExists = 0x80320009, ErrorFilterNotFound = 0x80320003, ErrorProviderNotFound = 0x80320005;
    private const uint ErrorSubLayerNotFound = 0x80320007;
    private const string FilterNamePrefix = "Nax-TaskManager Block ";

    /// <summary>
    /// Standard users may not read WFP, so every change also writes the list of blocked paths to the protected data
    /// folder, where the unelevated app reads it.
    /// </summary>
    private static string MirrorPath => Path.Combine(DataFolder.Path, "blocked-apps.txt");

    public static void Block(string path)
    {
        using (var engine = Engine.Open()) BlockCore(engine, path);
        WriteMirror();
    }

    public static void Unblock(string path)
    {
        using (var engine = Engine.Open()) UnblockCore(engine, path);
        WriteMirror();
    }

    private static void BlockCore(Engine engine, string path)
    {
        Transaction(engine, () =>
        {
            EnsureProviderAndSubLayer(engine);
            foreach (Guid layer in new[] { LayerConnectV4, LayerConnectV6 })
            {
                uint result = AddFilter(engine, path, layer);
                if (result != 0 && result != ErrorAlreadyExists) throw new WfpException("FwpmFilterAdd0", result);
            }
        });
    }

    private static void UnblockCore(Engine engine, string path)
    {
        Transaction(engine, () =>
        {
            foreach (Guid layer in new[] { LayerConnectV4, LayerConnectV6 })
            {
                Guid key = FilterKey(path, layer);
                uint result = FwpmFilterDeleteByKey0(engine.Handle, ref key);
                if (result != 0 && result != ErrorFilterNotFound) throw new WfpException("FwpmFilterDeleteByKey0", result);
            }
        });
    }

    /// <summary>
    /// Executable paths blocked by this app (lower case): from WFP itself when elevated, otherwise from the mirror file.
    /// </summary>
    public static HashSet<string> ReadBlockedPaths()
    {
        try
        {
            return ReadFilters();
        }
        catch (WfpException ex) when (ex.Code == 5)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(MirrorPath)) paths.UnionWith(File.ReadAllLines(MirrorPath).Where(line => line.Length > 0));
            }
            catch (Exception readError) when (readError is IOException or UnauthorizedAccessException)
            {
            }
            return paths;
        }
    }

    private static void WriteMirror()
    {
        HashSet<string> paths = ReadFilters();
        DataFolder.EnsureSecured();
        string temporary = MirrorPath + ".tmp";
        File.WriteAllLines(temporary, paths.Order(StringComparer.OrdinalIgnoreCase));
        File.Move(temporary, MirrorPath, overwrite: true);
    }

    private static HashSet<string> ReadFilters()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var engine = Engine.Open();
        IntPtr providerKey = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        IntPtr template = Marshal.AllocHGlobal(Marshal.SizeOf<FilterEnumTemplate>());
        try
        {
            Marshal.StructureToPtr(ProviderKey, providerKey, false);
            var enumTemplate = new FilterEnumTemplate
            {
                ProviderKey = providerKey,
                LayerKey = LayerConnectV4,
                EnumType = 0, // FWP_FILTER_ENUM_FULLY_CONTAINED
                Flags = 0,
                ActionMask = 0xFFFFFFFF
            };
            Marshal.StructureToPtr(enumTemplate, template, false);
            uint result = FwpmFilterCreateEnumHandle0(engine.Handle, template, out IntPtr enumHandle);
            if (result == ErrorProviderNotFound) return paths; // nothing was ever blocked
            if (result != 0) throw new WfpException("FwpmFilterCreateEnumHandle0", result);
            try
            {
                while (true)
                {
                    result = FwpmFilterEnum0(engine.Handle, enumHandle, 256, out IntPtr entries, out uint count);
                    if (result != 0) throw new WfpException("FwpmFilterEnum0", result);
                    try
                    {
                        for (int index = 0; index < count; index++)
                        {
                            IntPtr filter = Marshal.ReadIntPtr(entries, index * IntPtr.Size);
                            var data = Marshal.PtrToStructure<Filter>(filter);
                            string? description = Marshal.PtrToStringUni(data.DisplayData.Description);
                            if (!string.IsNullOrEmpty(description)) paths.Add(description);
                        }
                    }
                    finally
                    {
                        if (entries != IntPtr.Zero) FwpmFreeMemory0(ref entries);
                    }
                    if (count < 256) break;
                }
            }
            finally
            {
                FwpmFilterDestroyEnumHandle0(engine.Handle, enumHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(providerKey);
            Marshal.FreeHGlobal(template);
        }
        return paths;
    }

    private static uint AddFilter(Engine engine, string path, Guid layer)
    {
        uint result = FwpmGetAppIdFromFileName0(path, out IntPtr appId);
        if (result != 0) throw new WfpException("FwpmGetAppIdFromFileName0", result);
        IntPtr providerKey = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        IntPtr condition = Marshal.AllocHGlobal(Marshal.SizeOf<FilterCondition>());
        IntPtr name = Marshal.StringToHGlobalUni(FilterNamePrefix + FirewallRules.RuleNameForPath(path)[^12..]);
        IntPtr description = Marshal.StringToHGlobalUni(path.ToLowerInvariant());
        try
        {
            Marshal.StructureToPtr(ProviderKey, providerKey, false);
            Marshal.StructureToPtr(new FilterCondition
            {
                FieldKey = ConditionAppId,
                MatchType = MatchEqual,
                ConditionValue = new Value { Type = TypeByteBlob, Data = appId }
            }, condition, false);
            var filter = new Filter
            {
                FilterKey = FilterKey(path, layer),
                DisplayData = new DisplayData { Name = name, Description = description },
                Flags = FlagPersistent,
                ProviderKey = providerKey,
                LayerKey = layer,
                SubLayerKey = SubLayerKey,
                Weight = new Value { Type = TypeUInt8, Data = (IntPtr)15 },
                NumFilterConditions = 1,
                FilterCondition = condition,
                Action = new FilterAction { Type = ActionBlock }
            };
            return FwpmFilterAdd0(engine.Handle, ref filter, IntPtr.Zero, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(providerKey);
            Marshal.FreeHGlobal(condition);
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(description);
            FwpmFreeMemory0(ref appId);
        }
    }

    private static void EnsureProviderAndSubLayer(Engine engine)
    {
        IntPtr name = Marshal.StringToHGlobalUni("Nax-TaskManager");
        IntPtr description = Marshal.StringToHGlobalUni("Outbound blocks set in Nax-TaskManager");
        IntPtr providerKey = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        try
        {
            var provider = new Provider { ProviderKey = ProviderKey, DisplayData = new DisplayData { Name = name, Description = description }, Flags = FlagPersistent };
            uint result = FwpmProviderAdd0(engine.Handle, ref provider, IntPtr.Zero);
            if (result != 0 && result != ErrorAlreadyExists) throw new WfpException("FwpmProviderAdd0", result);

            Marshal.StructureToPtr(ProviderKey, providerKey, false);
            var subLayer = new SubLayer { SubLayerKey = SubLayerKey, DisplayData = provider.DisplayData, Flags = FlagPersistent, ProviderKey = providerKey, Weight = 0x8000 };
            result = FwpmSubLayerAdd0(engine.Handle, ref subLayer, IntPtr.Zero);
            if (result != 0 && result != ErrorAlreadyExists) throw new WfpException("FwpmSubLayerAdd0", result);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(description);
            Marshal.FreeHGlobal(providerKey);
        }
    }

    /// <summary>Stable per path and layer, so unblocking needs no lookup.</summary>
    private static Guid FilterKey(string path, Guid layer)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant() + "|" + layer.ToString("N")));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5-style
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }

    private static void Transaction(Engine engine, Action body)
    {
        uint result = FwpmTransactionBegin0(engine.Handle, 0);
        if (result != 0) throw new WfpException("FwpmTransactionBegin0", result);
        try
        {
            body();
        }
        catch
        {
            FwpmTransactionAbort0(engine.Handle);
            throw;
        }
        result = FwpmTransactionCommit0(engine.Handle);
        if (result != 0) throw new WfpException("FwpmTransactionCommit0", result);
    }

    private sealed class Engine : IDisposable
    {
        public IntPtr Handle { get; private init; }

        public static Engine Open()
        {
            uint result = FwpmEngineOpen0(null, RpcAuthnWinNt, IntPtr.Zero, IntPtr.Zero, out IntPtr handle);
            if (result != 0) throw new WfpException("FwpmEngineOpen0", result);
            return new Engine { Handle = handle };
        }

        public void Dispose() => FwpmEngineClose0(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayData
    {
        public IntPtr Name;
        public IntPtr Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByteBlob
    {
        public uint Size;
        public IntPtr Data;
    }

    /// <summary>FWP_VALUE0 / FWP_CONDITION_VALUE0: a type tag and an 8-byte union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Value
    {
        public uint Type;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterCondition
    {
        public Guid FieldKey;
        public uint MatchType;
        public Value ConditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterAction
    {
        public uint Type;
        public Guid FilterTypeOrCalloutKey;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct ContextUnion
    {
        [FieldOffset(0)] public ulong RawContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Filter
    {
        public Guid FilterKey;
        public DisplayData DisplayData;
        public uint Flags;
        public IntPtr ProviderKey;
        public ByteBlob ProviderData;
        public Guid LayerKey;
        public Guid SubLayerKey;
        public Value Weight;
        public uint NumFilterConditions;
        public IntPtr FilterCondition;
        public FilterAction Action;
        public ContextUnion Context;
        public IntPtr Reserved;
        public ulong FilterId;
        public Value EffectiveWeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Provider
    {
        public Guid ProviderKey;
        public DisplayData DisplayData;
        public uint Flags;
        public ByteBlob ProviderData;
        public IntPtr ServiceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubLayer
    {
        public Guid SubLayerKey;
        public DisplayData DisplayData;
        public uint Flags;
        public IntPtr ProviderKey;
        public ByteBlob ProviderData;
        public ushort Weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterEnumTemplate
    {
        public IntPtr ProviderKey;
        public Guid LayerKey;
        public uint EnumType;
        public uint Flags;
        public IntPtr ProviderContextTemplate;
        public uint NumFilterConditions;
        public IntPtr FilterCondition;
        public uint ActionMask;
        public IntPtr CalloutKey;
    }

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmProviderAdd0(IntPtr engineHandle, ref Provider provider, IntPtr securityDescriptor);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref SubLayer subLayer, IntPtr securityDescriptor);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref Filter filter, IntPtr securityDescriptor, out ulong id);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

    [DllImport("fwpuclnt.dll")]
    private static extern void FwpmFreeMemory0(ref IntPtr pointer);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterCreateEnumHandle0(IntPtr engineHandle, IntPtr enumTemplate, out IntPtr enumHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterEnum0(IntPtr engineHandle, IntPtr enumHandle, uint numEntriesRequested, out IntPtr entries, out uint numEntriesReturned);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterDestroyEnumHandle0(IntPtr engineHandle, IntPtr enumHandle);
}

public sealed class WfpException(string function, uint code)
    : Exception($"{function} failed with 0x{code:X8}{(code == 5 ? " (administrator rights needed)" : "")}.")
{
    public uint Code { get; } = code;
}
