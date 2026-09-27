using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BetterTaskManager.Fluent.Services;

/// <summary>
/// Executable icons by path. Extraction runs on the thread pool; the returned <see cref="BitmapImage"/> is
/// created immediately and fills in once decoding finishes, so rows never wait for icons.
/// </summary>
public static class IconCache
{
    private const int IconSize = 32;
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static BitmapImage? s_default;

    public static ImageSource Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Default;
        if (Cache.TryGetValue(path, out BitmapImage? cached)) return cached;

        var image = new BitmapImage { DecodePixelWidth = IconSize };
        Cache[path] = image;
        Load(image, () => TryExtractPng(path) ?? ExtractStockApplicationPng());
        return image;
    }

    public static ImageSource Default
    {
        get
        {
            if (s_default is null)
            {
                s_default = new BitmapImage { DecodePixelWidth = IconSize };
                Load(s_default, ExtractStockApplicationPng);
            }
            return s_default;
        }
    }

    private static void Load(BitmapImage image, Func<byte[]?> extract)
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() =>
        {
            byte[]? png;
            try
            {
                png = extract();
            }
            catch
            {
                png = null;
            }
            if (png is null) return;

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    using var stream = new MemoryStream(png);
                    await image.SetSourceAsync(stream.AsRandomAccessStream());
                }
                catch
                {
                    // A broken icon resource simply leaves the row without an icon.
                }
            });
        });
    }

    /// <summary>Returns null for files without a usable icon, so the caller can fall back to the stock icon.</summary>
    private static byte[]? TryExtractPng(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractIcon(path, 0, IconSize);
            return icon is null ? null : ToPng(icon);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ExternalException)
        {
            return null;
        }
    }

    private static byte[]? ExtractStockApplicationPng()
    {
        var info = new StockIconInfo { Size = (uint)Marshal.SizeOf<StockIconInfo>() };
        if (SHGetStockIconInfo(StockIconApplication, StockIconFlagIcon | StockIconFlagLarge, ref info) != 0 || info.Icon == IntPtr.Zero) return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(info.Icon);
            return ToPng(icon);
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private static byte[] ToPng(System.Drawing.Icon icon)
    {
        using System.Drawing.Bitmap bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private const uint StockIconApplication = 2;
    private const uint StockIconFlagIcon = 0x100;
    private const uint StockIconFlagLarge = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIconInfo
    {
        public uint Size;
        public IntPtr Icon;
        public int SystemImageIndex;
        public int IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Path;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint stockIconId, uint flags, ref StockIconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
