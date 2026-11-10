using BioTap.Core;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Win32;

namespace BioTap.App.Shell;

static class Placement
{
    /// <summary>The display under the cursor (the one the user is looking at).</summary>
    public static DisplayArea UnderCursor()
        => DisplayArea.GetFromPoint(PInvoke.GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : new PointInt32(0, 0), DisplayAreaFallback.Primary);

    /// <summary>Centered 8 px above the taskbar of <paramref name="area"/>, from its real work area.</summary>
    public static RectInt32 AboveTaskbar(DisplayArea area, SizeInt32 size)
    {
        var (x, y) = TaskbarGeometry.Place(Rect(area.WorkArea), Rect(area.OuterBounds), size.Width, size.Height);
        return new RectInt32(x, y, size.Width, size.Height);
    }

    /// <summary>Centered 8 px above <paramref name="anchor"/> (the open panel), kept inside the work area.</summary>
    public static RectInt32 Above(DisplayArea area, RectInt32 anchor, SizeInt32 size)
    {
        var (x, y) = TaskbarGeometry.PlaceAbove(Rect(area.WorkArea), Rect(anchor), size.Width, size.Height);
        return new RectInt32(x, y, size.Width, size.Height);
    }

    static ScreenRect Rect(RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);
}
