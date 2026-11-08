using BioTap.Core;
using BioTap.Core.Actions;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Overlay;

/// <summary>
/// Feedback pill above the taskbar (spec §4). Never takes focus or clicks: the action may target the window the
/// user is in (paste, always-on-top, minimize others).
/// </summary>
sealed class OverlayWindow
{
    const int Height = 48;
    readonly Window window;
    readonly FontIcon icon = new() { FontSize = 20 };
    readonly TextBlock title = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock detail = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherQueueTimer hideTimer;

    public OverlayWindow()
    {
        window = new Window
        {
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            Content = new Border
            {
                Padding = new Thickness(16, 0, 16, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { icon, title, detail } },
            },
        };
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        window.AppWindow.IsShownInSwitchers = false;

        var hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        var ex = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_LAYERED);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);

        hideTimer = window.DispatcherQueue.CreateTimer();
        hideTimer.IsRepeating = false;
        hideTimer.Tick += (_, _) => window.AppWindow.Hide();
    }

    /// <summary>Call on the UI thread.</summary>
    public void Show(ActionOutcome o, TimeSpan duration)
    {
        icon.Glyph = Glyphs.For(o);
        icon.Foreground = o.Kind == OutcomeKind.Failed ? new SolidColorBrush(Colors.IndianRed) : null;
        title.Text = o.Title;
        detail.Text = o.Detail ?? "";
        detail.Visibility = o.Detail is null ? Visibility.Collapsed : Visibility.Visible;

        // Width from the text, capped; position from the work area of the monitor under the cursor
        var scale = window.Content.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)Math.Min(560, (80 + 8 * (o.Title.Length + (o.Detail?.Length ?? 0))) * scale);
        var height = (int)(Height * scale);
        var area = DisplayArea.GetFromPoint(Cursor(), DisplayAreaFallback.Primary);
        var (x, y) = TaskbarGeometry.Place(
            new ScreenRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height),
            new ScreenRect(area.OuterBounds.X, area.OuterBounds.Y, area.OuterBounds.Width, area.OuterBounds.Height),
            width, height);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        window.AppWindow.Show(activateWindow: false);

        hideTimer.Stop();
        hideTimer.Interval = duration;
        hideTimer.Start();
    }

    static PointInt32 Cursor() => PInvoke.GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : new PointInt32(0, 0);
}
