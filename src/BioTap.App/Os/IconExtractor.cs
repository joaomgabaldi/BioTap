using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BioTap.Core.Config;

namespace BioTap.App.Os;

/// <summary>Program icon of an existing local file as PNG; null for URLs, missing files or any failure. Never logs the path.</summary>
sealed class IconExtractor : IIconExtractor
{
    public byte[]? ExtractPng(string file, int size)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(file).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path) || !(File.Exists(path) || Directory.Exists(path))) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var src = icon.ToBitmap();
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
