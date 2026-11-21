using Microsoft.Win32;

namespace BetterTaskManager.Core.Native;

/// <summary>Saved application/boot prefetch flags. Some Windows builds reject the MMAgent prefetch command.</summary>
internal static class PrefetchSettings
{
    internal const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
    private const string ValueName = "EnablePrefetcher";

    internal static int? ReadMode()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath);
        object? value = key?.GetValue(ValueName);
        if (value is null) return null;
        if (value is int mode && mode is >= 0 and <= 3 && key!.GetValueKind(ValueName) == RegistryValueKind.DWord) return mode;
        throw new InvalidOperationException("Windows has an unrecognized Prefetch configuration. It was left unchanged.");
    }

    internal static bool ApplicationsEnabled(int? mode, bool reported) => mode is { } value ? (value & 1) != 0 : reported;

    internal static int WithApplications(int mode, bool enabled)
    {
        if (mode is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        return enabled ? mode | 1 : mode & ~1;
    }

    internal static void WriteMode(int mode)
    {
        if (mode is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("The Windows Prefetch configuration is unavailable.");
        key.SetValue(ValueName, mode, RegistryValueKind.DWord);
        if (ReadMode() != mode) throw new InvalidOperationException("Windows did not save the requested Prefetch setting.");
    }
}
