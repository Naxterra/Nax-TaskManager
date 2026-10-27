using System.Runtime.InteropServices;

namespace BetterTaskManager.Core.Firewall;

/// <summary>One WFP filter of an app policy, before it is written: what it matches and whether it permits or blocks.</summary>
public sealed record PolicyFilter(bool Inbound, bool V6, bool Permit, string Reason, IReadOnlyList<Cidr> Remote, IReadOnlyList<int> Protocols, int PortFrom, int PortTo);

/// <summary>
/// Per-app rules as WFP filters. Each app gets its own ordered filter list per direction and IP version: the
/// switches (block Internet, LAN, this PC, inbound) first, then the rules in their order. Filters of one list get
/// descending weights in this app's sublayer, so the first matching one decides, exactly as the rule list reads.
/// All of them stay below the whole-app block, which therefore always wins. Outgoing filters sit at the ALE connect
/// layers, incoming ones at the ALE receive/accept layers.
/// </summary>
public static partial class WfpBlocker
{
    private static readonly Guid LayerAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    private static readonly Guid LayerAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");
    private static readonly Guid ConditionRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    private static readonly Guid ConditionRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    private static readonly Guid ConditionLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
    private static readonly Guid ConditionProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    private static readonly Guid[] AllLayers = [LayerConnectV4, LayerConnectV6, LayerAcceptV4, LayerAcceptV6];

    private const uint ActionPermit = 0x1002; // FWP_ACTION_PERMIT | FWP_ACTION_FLAG_TERMINATING
    private const uint TypeUInt16 = 2, TypeUInt64 = 4, TypeV4AddrMask = 0x100, TypeV6AddrMask = 0x101, TypeRange = 0x102;
    private const uint MatchRange = 5;
    private const string RuleDescriptionPrefix = "rule|";
    /// <summary>The whole-app block uses the top weight range (15); rules use the one below it.</summary>
    private const ulong RuleWeightBase = 14UL << 60;
    private const ulong RuleWeightTop = 0x0FFF_FFFF;

    /// <summary>The filters a policy needs, grouped by layer and in decision order within each layer.</summary>
    public static List<PolicyFilter> Compile(AppNetworkPolicy policy)
    {
        var filters = new List<PolicyFilter>();
        foreach (bool inbound in new[] { false, true })
        {
            foreach (bool v6 in new[] { false, true })
            {
                var layer = new List<PolicyFilter>();
                void Block(string reason, List<Cidr> remote) => layer.Add(new PolicyFilter(inbound, v6, false, reason, remote, [], 0, 0));
                List<Cidr> Of(IReadOnlyList<Cidr> scope) => scope.Where(range => range.V6 == v6).ToList();

                if (inbound && policy.BlockInbound) Block(PolicyReason.BlockInbound, Cidr.Complement(RuleScopes.Localhost, v6));
                if (policy.BlockLocalhost) Block(PolicyReason.BlockLocalhost, Of(RuleScopes.Localhost));
                if (policy.BlockLan) Block(PolicyReason.BlockLan, Of(RuleScopes.Lan));
                if (policy.BlockInternet) Block(PolicyReason.BlockInternet, Of(RuleScopes.Internet));
                foreach (NetworkRule rule in inbound ? policy.ParsedIncoming() : policy.ParsedOutgoing())
                {
                    if (!rule.AppliesTo(v6)) continue;
                    IReadOnlyList<Cidr> remote = rule.Target == RuleTargetKind.Any ? [] : rule.Ranges(v6);
                    layer.Add(new PolicyFilter(inbound, v6, rule.Allow, PolicyReason.ForRule(rule), remote, rule.Protocols(v6), rule.PortFrom, rule.PortTo));
                }
                // An allow with no block below it changes nothing, since everything not blocked is allowed.
                while (layer.Count > 0 && layer[^1].Permit) layer.RemoveAt(layer.Count - 1);
                filters.AddRange(layer);
            }
        }
        return filters;
    }

    /// <summary>
    /// Replaces the app's rule filters with the ones its policy needs, in one transaction, so the old set stays if
    /// anything fails. An empty policy removes them. Does not touch the whole-app block. Needs administrator rights.
    /// </summary>
    public static void ApplyPolicy(AppNetworkPolicy policy)
    {
        List<PolicyFilter> filters = Compile(policy);
        string description = RuleDescriptionPrefix + policy.Path.ToLowerInvariant();
        using var engine = Engine.Open();
        Transaction(engine, () =>
        {
            EnsureProviderAndSubLayer(engine);
            foreach (Guid layer in AllLayers)
            {
                foreach (WfpFilterInfo existing in EnumerateFilters(engine, layer))
                {
                    if (existing.Description != description) continue;
                    Guid key = existing.Key;
                    uint deleted = FwpmFilterDeleteByKey0(engine.Handle, ref key);
                    if (deleted != 0 && deleted != ErrorFilterNotFound) throw new WfpException("FwpmFilterDeleteByKey0", deleted);
                }
            }
            if (filters.Count == 0) return;

            uint result = FwpmGetAppIdFromFileName0(policy.Path, out IntPtr appId);
            if (result != 0) throw new WfpException("FwpmGetAppIdFromFileName0", result);
            try
            {
                var positions = new Dictionary<Guid, int>();
                foreach (PolicyFilter filter in filters)
                {
                    Guid layer = filter.Inbound ? (filter.V6 ? LayerAcceptV6 : LayerAcceptV4) : (filter.V6 ? LayerConnectV6 : LayerConnectV4);
                    int index = positions.GetValueOrDefault(layer);
                    positions[layer] = index + 1;
                    result = AddPolicyFilter(engine, appId, policy.Path, description, layer, index, filter);
                    if (result != 0) throw new WfpException("FwpmFilterAdd0", result);
                }
            }
            finally
            {
                FwpmFreeMemory0(ref appId);
            }
        });
    }

    /// <summary>Deletes every rule filter of every app in one transaction; whole-app blocks stay. Needs administrator rights.</summary>
    public static void RemoveAllPolicies()
    {
        using var engine = Engine.Open();
        Transaction(engine, () =>
        {
            foreach (Guid layer in AllLayers)
            {
                foreach (WfpFilterInfo filter in EnumerateFilters(engine, layer))
                {
                    if (!filter.Description.StartsWith(RuleDescriptionPrefix, StringComparison.Ordinal)) continue;
                    Guid key = filter.Key;
                    uint deleted = FwpmFilterDeleteByKey0(engine.Handle, ref key);
                    if (deleted != 0 && deleted != ErrorFilterNotFound) throw new WfpException("FwpmFilterDeleteByKey0", deleted);
                }
            }
        });
    }

    /// <summary>Paths (lower case) that have rule filters, from WFP. Needs administrator rights.</summary>
    public static HashSet<string> ReadPolicyPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var engine = Engine.Open();
        foreach (Guid layer in AllLayers)
        {
            foreach (WfpFilterInfo filter in EnumerateFilters(engine, layer))
            {
                if (filter.Description.StartsWith(RuleDescriptionPrefix, StringComparison.Ordinal)) paths.Add(filter.Description[RuleDescriptionPrefix.Length..]);
            }
        }
        return paths;
    }

    private static uint AddPolicyFilter(Engine engine, IntPtr appId, string path, string description, Guid layer, int index, PolicyFilter policyFilter)
    {
        var allocations = new List<IntPtr>();
        IntPtr Allocate(int size)
        {
            IntPtr memory = Marshal.AllocHGlobal(size);
            allocations.Add(memory);
            return memory;
        }

        try
        {
            var conditions = new List<FilterCondition>
            {
                new() { FieldKey = ConditionAppId, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeByteBlob, Data = appId } }
            };
            foreach (Cidr range in policyFilter.Remote)
            {
                IntPtr mask;
                if (range.V6)
                {
                    // FWP_V6_ADDR_AND_MASK: 16 address bytes, then the prefix length.
                    mask = Allocate(17);
                    byte[] address = range.Address.GetAddressBytes();
                    Marshal.Copy(address, 0, mask, 16);
                    Marshal.WriteByte(mask, 16, (byte)range.Prefix);
                }
                else
                {
                    // FWP_V4_ADDR_AND_MASK: address and mask as host-order numbers.
                    mask = Allocate(8);
                    uint maskBits = range.Prefix == 0 ? 0 : uint.MaxValue << (32 - range.Prefix);
                    Marshal.WriteInt32(mask, 0, unchecked((int)(uint)range.Network));
                    Marshal.WriteInt32(mask, 4, unchecked((int)maskBits));
                }
                conditions.Add(new FilterCondition { FieldKey = ConditionRemoteAddress, MatchType = MatchEqual, ConditionValue = new Value { Type = range.V6 ? TypeV6AddrMask : TypeV4AddrMask, Data = mask } });
            }
            foreach (int protocol in policyFilter.Protocols)
            {
                conditions.Add(new FilterCondition { FieldKey = ConditionProtocol, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeUInt8, Data = protocol } });
            }
            if (policyFilter.PortFrom > 0)
            {
                // Incoming rules name the port the connection arrives at, which is this PC's port.
                Guid field = policyFilter.Inbound ? ConditionLocalPort : ConditionRemotePort;
                if (policyFilter.PortFrom == policyFilter.PortTo)
                {
                    conditions.Add(new FilterCondition { FieldKey = field, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeUInt16, Data = policyFilter.PortFrom } });
                }
                else
                {
                    // FWP_RANGE0: two FWP_VALUE0 of 16 bytes each (type, padding, 8-byte union).
                    IntPtr range = Allocate(32);
                    for (int offset = 0; offset < 32; offset += 8) Marshal.WriteInt64(range, offset, 0);
                    Marshal.WriteInt32(range, 0, (int)TypeUInt16);
                    Marshal.WriteInt16(range, 8, unchecked((short)policyFilter.PortFrom));
                    Marshal.WriteInt32(range, 16, (int)TypeUInt16);
                    Marshal.WriteInt16(range, 24, unchecked((short)policyFilter.PortTo));
                    conditions.Add(new FilterCondition { FieldKey = field, MatchType = MatchRange, ConditionValue = new Value { Type = TypeRange, Data = range } });
                }
            }

            int conditionSize = Marshal.SizeOf<FilterCondition>();
            IntPtr conditionArray = Allocate(conditionSize * conditions.Count);
            for (int position = 0; position < conditions.Count; position++) Marshal.StructureToPtr(conditions[position], conditionArray + position * conditionSize, false);

            IntPtr weight = Allocate(8);
            Marshal.WriteInt64(weight, unchecked((long)(RuleWeightBase + RuleWeightTop - (ulong)index)));
            IntPtr providerKey = Allocate(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ProviderKey, providerKey, false);
            IntPtr name = Marshal.StringToHGlobalUni(PolicyReason.NamePrefix + policyFilter.Reason);
            allocations.Add(name);
            IntPtr descriptionText = Marshal.StringToHGlobalUni(description);
            allocations.Add(descriptionText);

            var filter = new Filter
            {
                FilterKey = FilterKey(path + "|rule" + index, layer),
                DisplayData = new DisplayData { Name = name, Description = descriptionText },
                Flags = FlagPersistent,
                ProviderKey = providerKey,
                LayerKey = layer,
                SubLayerKey = SubLayerKey,
                Weight = new Value { Type = TypeUInt64, Data = weight },
                NumFilterConditions = (uint)conditions.Count,
                FilterCondition = conditionArray,
                Action = new FilterAction { Type = policyFilter.Permit ? ActionPermit : ActionBlock }
            };
            return FwpmFilterAdd0(engine.Handle, ref filter, IntPtr.Zero, out _);
        }
        finally
        {
            foreach (IntPtr memory in allocations) Marshal.FreeHGlobal(memory);
        }
    }

    private readonly record struct WfpFilterInfo(Guid Key, ulong Id, string Name, string Description);

    /// <summary>Every filter of this app's provider at one layer.</summary>
    private static List<WfpFilterInfo> EnumerateFilters(Engine engine, Guid layer)
    {
        var filters = new List<WfpFilterInfo>();
        IntPtr providerKey = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        IntPtr template = Marshal.AllocHGlobal(Marshal.SizeOf<FilterEnumTemplate>());
        try
        {
            Marshal.StructureToPtr(ProviderKey, providerKey, false);
            var enumTemplate = new FilterEnumTemplate
            {
                ProviderKey = providerKey,
                LayerKey = layer,
                EnumType = 0, // FWP_FILTER_ENUM_FULLY_CONTAINED
                Flags = 0,
                ActionMask = 0xFFFFFFFF
            };
            Marshal.StructureToPtr(enumTemplate, template, false);
            uint result = FwpmFilterCreateEnumHandle0(engine.Handle, template, out IntPtr enumHandle);
            if (result == ErrorProviderNotFound) return filters; // nothing was ever added
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
                            var data = Marshal.PtrToStructure<Filter>(Marshal.ReadIntPtr(entries, index * IntPtr.Size));
                            filters.Add(new WfpFilterInfo(data.FilterKey, data.FilterId,
                                Marshal.PtrToStringUni(data.DisplayData.Name) ?? "", Marshal.PtrToStringUni(data.DisplayData.Description) ?? ""));
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
        return filters;
    }
}
