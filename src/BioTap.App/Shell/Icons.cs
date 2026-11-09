using BioTap.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BioTap.App.Shell;

static class Icons
{
    const string FallbackGlyph = "";

    /// <summary>An "exe" icon with a cached file as an Image; anything else as a FontIcon (E8E5 when the exe icon is missing).</summary>
    public static FrameworkElement Element(IconRef icon, double size)
    {
        if (icon.Kind == "exe" && Load(icon) is { } image)
            return new Image { Source = image, Width = size, Height = size };
        var glyph = icon.GlyphText();
        return new FontIcon { Glyph = glyph.Length > 0 || icon.Kind != "exe" ? glyph : FallbackGlyph, FontSize = size };
    }

    static BitmapImage? Load(IconRef icon)
    {
        try
        {
            if (IconCache.PathFor(Program.DataFolder, icon.Value) is not { } path || !File.Exists(path)) return null;
            // Bytes through memory so the cached file is never kept locked
            var stream = new InMemoryRandomAccessStream();
            var s = stream.AsStreamForWrite();
            s.Write(File.ReadAllBytes(path));
            s.Flush();
            stream.Seek(0);
            var image = new BitmapImage();
            image.SetSource(stream);
            return image;
        }
        catch (Exception) { return null; }
    }
}
