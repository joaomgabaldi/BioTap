using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BioTap.Core.Config;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Os;

/// <summary>Program icon of an existing local file as PNG; null for URLs, missing files or any failure. Never logs the path.</summary>
sealed class IconExtractor : IIconExtractor
{
    public byte[]? ExtractPng(string file, int size)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(file).Replace('/', '\\');
            // Rule lives in Core; ExtractIconEx (not ExtractAssociatedIcon) so no per-user shell icon handler runs in this elevated app
            if (!IconCache.CanExtractFrom(path) || !File.Exists(path)) return null;
            var large = new HICON[1];
            if (PInvoke.ExtractIconEx(path, 0, large, default) == 0 || large[0].IsNull) return null;
            Bitmap src;
            try { using var icon = Icon.FromHandle((IntPtr)large[0]); src = icon.ToBitmap(); }
            finally { PInvoke.DestroyIcon(large[0]); }
            using var _ = src;
            using var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            using var ms = new MemoryStream();
            dst.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception) { return null; }
    }
}
