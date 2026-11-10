using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using BioTap.Core.Text;

namespace BioTap.App.Shell;

/// <summary>
/// Shared look: the palette lives in App.xaml (BioTap* keys) and is read here; motion and glass values live here.
/// </summary>
static class Theme
{
    /// <summary>One of our own BioTap* brushes from App.xaml (a missing key is a bug, so it throws).</summary>
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Windows.UI.Color Color(string key) => (Windows.UI.Color)Application.Current.Resources[key];

    public static Style? Style(string key)
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;


    // Panel and confirmation share one motion: rise 7 px with a fade in, sink 7 px with a fade out
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(150);
    public const double MotionOffset = 7;

    /// <summary>Glass surface of the panel and the confirmation: #262626 at 75%.</summary>
    public static readonly Windows.UI.Color GlassTint = ColorHelper.FromArgb(0xFF, 0x26, 0x26, 0x26);
    public const float GlassTintOpacity = 0.75f;
    /// <summary>Subtle light border drawn by DWM around the glass windows (#FFFFFF at ~15% over the glass).</summary>
    public static readonly Windows.UI.Color GlassBorder = ColorHelper.FromArgb(0xFF, 0x4A, 0x4A, 0x4A);

    public static string Taps(int n) => Strings.Current.Taps(n);
}
