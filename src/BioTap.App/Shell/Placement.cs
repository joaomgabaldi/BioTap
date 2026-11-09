using BioTap.Core;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace BioTap.App.Shell;

static class Placement
{
    /// <summary>
    /// Centers the window above the taskbar of the monitor under the cursor. The window is first moved to that
    /// monitor so <paramref name="sizeAt"/> gets that monitor's DPI scale (monitors can differ).
    /// </summary>
    public static void AboveTaskbar(AppWindow window, HWND hwnd, Func<double, (int Width, int Height)> sizeAt)
    {
        var area = DisplayArea.GetFromPoint(Cursor(), DisplayAreaFallback.Primary);
        window.Move(new PointInt32(area.WorkArea.X, area.WorkArea.Y));
        var (width, height) = sizeAt(PInvoke.GetDpiForWindow(hwnd) / 96.0);
        var (x, y) = TaskbarGeometry.Place(
            new ScreenRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height),
            new ScreenRect(area.OuterBounds.X, area.OuterBounds.Y, area.OuterBounds.Width, area.OuterBounds.Height),
            width, height);
        window.MoveAndResize(new RectInt32(x, y, width, height));
    }

    static PointInt32 Cursor() => PInvoke.GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : new PointInt32(0, 0);
}
