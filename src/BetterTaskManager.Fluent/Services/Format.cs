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

    public static string Gigabytes(long bytes) => (bytes / 1073741824d).ToString("0.0", Culture) + " GB";

    public static string Percent(double value) => value.ToString("0.0", Culture) + "%";

    public static string WholePercent(double value) => Math.Round(value).ToString("0", Culture) + "%";

    public static string Rate(double bytesPerSecond) => (bytesPerSecond / 1048576d).ToString("0.0", Culture) + " MB/s";

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
