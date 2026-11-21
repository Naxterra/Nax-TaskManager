using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BetterTaskManager.Core.Native;

/// <summary>Matches VM memory processes to the Host Compute Service by their VM security identifier.</summary>
internal sealed class VirtualMachineOwners
{
    private Dictionary<(int Pid, long Creation), string> owners = [];
    private HashSet<(int Pid, long Creation)> knownVms = [];
    private long readAt;

    public Dictionary<(int Pid, long Creation), string> Read(IReadOnlyList<RawProcess> processes)
    {
        var currentVms = new HashSet<(int, long)>();
        foreach (RawProcess process in processes)
        {
            if (IsMemoryProcess(process.Name)) currentVms.Add((process.Pid, process.CreateTime));
        }
        if (currentVms.Count == 0)
        {
            owners.Clear();
            knownVms.Clear();
            readAt = 0;
            return owners;
        }

        bool hasUnseen = !currentVms.IsSubsetOf(knownVms);
        if (!hasUnseen && readAt != 0 && Stopwatch.GetElapsedTime(readAt) < TimeSpan.FromSeconds(10)) return owners;
        readAt = Stopwatch.GetTimestamp();
        knownVms = currentVms;
        var identities = new Dictionary<int, Guid>();
        ProcessUsers.ReadVmIdentities(identities);
        owners = Match(processes, identities, identities.Count > 0 ? ReadComputeOwners() : []);
        return owners;
    }

    internal static bool IsMemoryProcess(string name)
    {
        ReadOnlySpan<char> stem = name.AsSpan();
        if (stem.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        return stem.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase);
    }

    internal static Dictionary<(int Pid, long Creation), string> Match(IReadOnlyList<RawProcess> processes,
        IReadOnlyDictionary<int, Guid> identities, IReadOnlyDictionary<Guid, string> computeOwners)
    {
        var result = new Dictionary<(int, long), string>();
        foreach (RawProcess process in processes)
        {
            if (IsMemoryProcess(process.Name) && process.CreateTime > 0 && identities.TryGetValue(process.Pid, out Guid id)
                && computeOwners.TryGetValue(id, out string? owner)) result[(process.Pid, process.CreateTime)] = owner;
        }
        return result;
    }

    internal static bool TryReadVmId(ReadOnlySpan<byte> sid, out Guid id)
    {
        id = Guid.Empty;
        // S-1-5-83-1-<four little-endian DWORDs of the VM ID>.
        ReadOnlySpan<byte> prefix = [1, 6, 0, 0, 0, 0, 0, 5, 83, 0, 0, 0, 1, 0, 0, 0];
        if (sid.Length != 32 || !sid.StartsWith(prefix)) return false;
        id = new Guid(sid[16..]);
        return id != Guid.Empty;
    }

    private static Dictionary<Guid, string> ReadComputeOwners()
    {
        try
        {
            IntPtr operation = HcsCreateOperation(IntPtr.Zero, IntPtr.Zero);
            if (operation == IntPtr.Zero) return [];
            IntPtr result = IntPtr.Zero;
            try
            {
                if (HcsEnumerateComputeSystems(null, operation) != 0 || HcsWaitForOperationResult(operation, 1000, out result) != 0)
                    return [];
                return Marshal.PtrToStringUni(result) is { } json ? ParseComputeOwners(json) : [];
            }
            finally
            {
                if (result != IntPtr.Zero) LocalFree(result);
                HcsCloseOperation(operation);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or JsonException) { return []; }
    }

    internal static Dictionary<Guid, string> ParseComputeOwners(string json)
    {
        using var document = JsonDocument.Parse(json);
        var owners = new Dictionary<Guid, string>();
        var ambiguous = new HashSet<Guid>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return owners;
        foreach (JsonElement system in document.RootElement.EnumerateArray())
        {
            if (Field(system, "SystemType") != "VirtualMachine" || !Guid.TryParse(Field(system, "RuntimeId"), out Guid id) || id == Guid.Empty)
                continue;
            string? owner = Field(system, "Owner")?.Trim();
            if (string.IsNullOrEmpty(owner) || owner.Length > 100 || owner.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
                continue;

            string? systemId = Field(system, "Id");
            if (owner.StartsWith("cowork-vm-", StringComparison.OrdinalIgnoreCase) && owner.Equals(systemId, StringComparison.OrdinalIgnoreCase))
                owner = "Cowork VM";
            if (!owners.TryAdd(id, owner) && owners[id] != owner) ambiguous.Add(id);
        }
        foreach (Guid id in ambiguous) owners.Remove(id);
        return owners;
    }

    private static string? Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    [DllImport("computecore.dll")] private static extern IntPtr HcsCreateOperation(IntPtr context, IntPtr callback);
    [DllImport("computecore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int HcsEnumerateComputeSystems(string? query, IntPtr operation);
    [DllImport("computecore.dll")] private static extern int HcsWaitForOperationResult(IntPtr operation, uint timeoutMs, out IntPtr result);
    [DllImport("computecore.dll")] private static extern void HcsCloseOperation(IntPtr operation);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
}
