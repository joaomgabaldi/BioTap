using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BioTap.App.Shell;

/// <summary>
/// Theme resources with a fallback. Brushes such as DividerStrokeColorDefaultBrush live in theme dictionaries that
/// a plain Resources[key] lookup may not reach, and a missing key throws.
/// </summary>
static class Theme
{
    public static Brush Brush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush b ? b : new SolidColorBrush(fallback);

    public static Style? Style(string key)
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    public static Windows.UI.Color Divider => ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    public static Windows.UI.Color Card => ColorHelper.FromArgb(0x0D, 0xFF, 0xFF, 0xFF);
}
