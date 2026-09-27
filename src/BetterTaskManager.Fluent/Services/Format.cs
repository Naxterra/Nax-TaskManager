using System.Globalization;

namespace BetterTaskManager.Fluent.Services;

internal static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static string Memory(long bytes)
    {
        double megabytes = bytes / 1048576d;
        return megabytes >= 1024
            ? (megabytes / 1024).ToString("0.0", Culture) + " GB"
            : megabytes.ToString("0.0", Culture) + " MB";
    }

    /// <summary>Data volume with a unit that fits: KB, MB or GB.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0", Culture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576d).ToString("0.0", Culture) + " MB";
        return (bytes / 1073741824d).ToString("0.00", Culture) + " GB";
    }

    public static string Gigabytes(long bytes) => (bytes / 1073741824d).ToString("0.0", Culture) + " GB";

    public static string Percent(double value) => value.ToString("0.0", Culture) + "%";

    public static string WholePercent(double value) => Math.Round(value).ToString("0", Culture) + "%";

    public static string Rate(double bytesPerSecond) => (bytesPerSecond / 1048576d).ToString("0.0", Culture) + " MB/s";

    /// <summary>Task Manager style: always Mbit/s with one decimal, so the column lines up.</summary>
    public static string Mbps(double bytesPerSecond) => (bytesPerSecond * 8 / 1_000_000).ToString("0.0", Culture) + " Mbit/s";

    public static string NetworkRate(double bytesPerSecond)
    {
        double bits = bytesPerSecond * 8;
        if (bits >= 1_000_000) return (bits / 1_000_000).ToString("0.0", Culture) + " Mbit/s";
        return (bits / 1000).ToString("0", Culture) + " kbit/s";
    }

    public static string Duration(TimeSpan value) =>
        $"{(int)value.TotalDays}:{value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";

    public static string Count(int value) => value.ToString("N0", Culture);
}
