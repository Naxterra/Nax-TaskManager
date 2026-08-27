[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $root "src\BetterTaskManager\assets\BetterTaskManager.ico"
$previewPath = Join-Path $root "src\BetterTaskManager\assets\BetterTaskManager-icon.png"

Add-Type -AssemblyName System.Drawing.Common
$gdiAssembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "System.Private.Windows.GdiPlus" } | Select-Object -First 1
$windowsCoreAssembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "System.Private.Windows.Core" } | Select-Object -First 1
$collectionsAssembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "System.Collections" } | Select-Object -First 1
$runtimeAssembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "System.Runtime" } | Select-Object -First 1
$drawingReferences = @([object].Assembly.Location, [System.Drawing.Bitmap].Assembly.Location, [System.Drawing.RectangleF].Assembly.Location, $gdiAssembly.Location, $windowsCoreAssembly.Location, $collectionsAssembly.Location, $runtimeAssembly.Location)
Add-Type -ReferencedAssemblies $drawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class BetterTaskManagerIconGenerator
{
    private static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = size / 256f;

            RectangleF outer = new RectangleF(8f * scale, 8f * scale, 240f * scale, 240f * scale);
            using (GraphicsPath outerPath = Rounded(outer, 48f * scale))
            using (var gradient = new LinearGradientBrush(outer, Color.FromArgb(255, 91, 55, 156), Color.FromArgb(255, 167, 139, 250), 45f))
            using (var border = new Pen(Color.FromArgb(230, 222, 211, 255), Math.Max(1f, 4f * scale)))
            {
                graphics.FillPath(gradient, outerPath);
                graphics.DrawPath(border, outerPath);
            }

            RectangleF window = new RectangleF(43f * scale, 46f * scale, 170f * scale, 164f * scale);
            using (GraphicsPath windowPath = Rounded(window, 18f * scale))
            using (var windowBrush = new SolidBrush(Color.FromArgb(205, 27, 22, 41)))
            using (var windowBorder = new Pen(Color.White, Math.Max(1.2f, 7f * scale)))
            {
                graphics.FillPath(windowBrush, windowPath);
                graphics.DrawPath(windowBorder, windowPath);
            }

            float titleY = 82f * scale;
            using (var titlePen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, 5f * scale)))
            {
                graphics.DrawLine(titlePen, 46f * scale, titleY, 210f * scale, titleY);
            }
            using (var dotBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                float dot = Math.Max(1.5f, 7f * scale);
                graphics.FillEllipse(dotBrush, 58f * scale, 61f * scale, dot, dot);
                graphics.FillEllipse(dotBrush, 74f * scale, 61f * scale, dot, dot);
                graphics.FillEllipse(dotBrush, 90f * scale, 61f * scale, dot, dot);
            }

            var bars = new[]
            {
                new RectangleF(64f * scale, 143f * scale, 20f * scale, 43f * scale),
                new RectangleF(96f * scale, 122f * scale, 20f * scale, 64f * scale),
                new RectangleF(128f * scale, 151f * scale, 20f * scale, 35f * scale),
                new RectangleF(160f * scale, 104f * scale, 20f * scale, 82f * scale)
            };
            using (var barBrush = new SolidBrush(Color.FromArgb(235, 216, 197, 255)))
            {
                foreach (RectangleF bar in bars)
                {
                    using (GraphicsPath barPath = Rounded(bar, Math.Max(1f, 5f * scale))) graphics.FillPath(barBrush, barPath);
                }
            }

            PointF[] trend =
            {
                new PointF(59f * scale, 151f * scale),
                new PointF(91f * scale, 133f * scale),
                new PointF(123f * scale, 145f * scale),
                new PointF(155f * scale, 111f * scale),
                new PointF(193f * scale, 98f * scale)
            };
            using (var trendPen = new Pen(Color.FromArgb(255, 105, 240, 192), Math.Max(1.5f, 8f * scale)))
            {
                trendPen.StartCap = LineCap.Round;
                trendPen.EndCap = LineCap.Round;
                trendPen.LineJoin = LineJoin.Round;
                graphics.DrawLines(trendPen, trend);
            }
        }
        return bitmap;
    }

    public static void Generate(string iconPath, string previewPath)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new byte[sizes.Length][];
        for (int index = 0; index < sizes.Length; index++)
        {
            int size = sizes[index];
            using (Bitmap bitmap = Render(size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images[index] = stream.ToArray();
            }
        }

        using (var file = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + (16 * sizes.Length);
            for (int index = 0; index < sizes.Length; index++)
            {
                int size = sizes[index];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[index].Length);
                writer.Write((uint)offset);
                offset += images[index].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }

        using (Bitmap preview = Render(256)) preview.Save(previewPath, ImageFormat.Png);
    }
}
'@

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $iconPath)) | Out-Null
[BetterTaskManagerIconGenerator]::Generate($iconPath, $previewPath)
Write-Host "Generated application icon: $iconPath"
Write-Host "Generated icon preview: $previewPath"
