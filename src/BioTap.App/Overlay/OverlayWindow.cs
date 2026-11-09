using BioTap.App.Shell;
using BioTap.Core.Actions;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    const int Height = 48, MaxWidth = 560, MinWidth = 160;
    readonly Window window;
    readonly HWND hwnd;
    readonly FontIcon icon = new() { FontSize = 20 };
    readonly TextBlock title = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock detail = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly DispatcherQueueTimer hideTimer;

    public OverlayWindow()
    {
        window = new Window
        {
            SystemBackdrop = new ActiveAcrylicBackdrop(),
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

        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        var ex = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        // TRANSPARENT needs LAYERED to pass clicks through; a layered window needs its alpha set or it is never drawn
        ex |= (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_LAYERED);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
        PInvoke.SetLayeredWindowAttributes(hwnd, default, 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        hideTimer = window.DispatcherQueue.CreateTimer();
        hideTimer.IsRepeating = false;
        hideTimer.Tick += (_, _) => Ui.Run(window.AppWindow.Hide, "overlay hide");
    }

    /// <summary>Call on the UI thread.</summary>
    public void Show(ActionOutcome o, TimeSpan duration)
    {
        icon.Glyph = Glyphs.For(o);
        icon.Foreground = o.Kind == OutcomeKind.Failed ? new SolidColorBrush(Colors.IndianRed) : null;
        title.Text = o.Title;
        detail.Text = o.Detail ?? "";
        detail.Visibility = o.Detail is null ? Visibility.Collapsed : Visibility.Visible;

        // Width estimated from the text in DIPs, capped, then scaled for the target monitor
        var dips = Math.Clamp(80 + 8 * (o.Title.Length + (o.Detail?.Length ?? 0)), MinWidth, MaxWidth);
        Placement.AboveTaskbar(window.AppWindow, hwnd, scale => ((int)(dips * scale), (int)(Height * scale)));
        window.AppWindow.Show(activateWindow: false);

        hideTimer.Stop();
        hideTimer.Interval = duration;
        hideTimer.Start();
    }
}
