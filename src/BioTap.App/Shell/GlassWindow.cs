using System.Diagnostics;
using System.Runtime.InteropServices;
using BioTap.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Shell;

/// <summary>
/// The glass window shared by the panel and the confirmation: tinted system acrylic, rounded DWM corners with a
/// subtle border, and the shared motion (rise 7 px + fade in, sink 7 px + fade out) driven frame by frame on the
/// UI thread. The fade is the layered-window alpha, which ends at 255: the acrylic keeps its own 75% tint and the
/// content stays fully opaque. The window is hidden only after the exit finishes.
/// Callers place the visible surface (the client area, where the XAML draws); the window around it also has the
/// invisible resize borders of the bordered presenter, which are added here from the window's real frame.
/// </summary>
sealed class GlassWindow
{
    static readonly UISettings SystemUi = new();

    readonly Reveal reveal = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    RectInt32 target; // the visible surface
    (int Left, int Top, int Right, int Bottom) frame; // invisible borders around it
    double offset;
    RectInt32 applied;
    Action? hidden;
    bool wanted; // shown or on its way in
    int entry; // cancels an entrance still waiting for its first drawn frame
    bool ticking;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer safety;

    public Window Window { get; }
    public HWND Hwnd { get; }
    public AppWindow AppWindow => Window.AppWindow;

    /// <summary>Shown or entering (not leaving).</summary>
    public bool IsShown => wanted;

    /// <summary>Final bounds of the visible surface while shown or entering.</summary>
    public RectInt32? Bounds => wanted ? target : null;

    public GlassWindow(UIElement content, WINDOW_EX_STYLE extraStyles = 0)
    {
        Window = new Window { SystemBackdrop = new ActiveAcrylicBackdrop(), Content = content };
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false); // DWM border, shadow and rounded corners, no caption
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        safety = Window.DispatcherQueue.CreateTimer();
        safety.IsRepeating = false;
        safety.Tick += (_, _) => Ui.Run(Frame, "glass safety");
        Hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(Window));

        // LAYERED carries the fade; a layered window needs its alpha set or it is never drawn
        var ex = PInvoke.GetWindowLong(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (int)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | extraStyles);
        PInvoke.SetWindowLong(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
        PInvoke.SetLayeredWindowAttributes(Hwnd, default, 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        // Windows 11 rounds to its own radius (8 px); a custom radius would need a window region, which breaks the acrylic
        var round = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        _ = PInvoke.DwmSetWindowAttribute(Hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref round, 1)));
        uint border = (uint)(Theme.GlassBorder.R | Theme.GlassBorder.G << 8 | Theme.GlassBorder.B << 16); // COLORREF 0x00BBGGRR
        _ = PInvoke.DwmSetWindowAttribute(Hwnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref border, 1)));
    }

    /// <summary>
    /// Scale of <paramref name="area"/>. A hidden window is first moved onto that display so the DPI is that
    /// display's (monitors can differ); a shown one stays where it is.
    /// </summary>
    public double ScaleOn(DisplayArea area)
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Move(new PointInt32(area.WorkArea.X, area.WorkArea.Y));
            applied = default; // the next Apply must move it back, even to the same bounds as last time
        }
        return PInvoke.GetDpiForWindow(Hwnd) / 96.0;
    }

    /// <summary>Surface size for content measured at <paramref name="dips"/> on a display at <paramref name="scale"/>.</summary>
    public static SizeInt32 SizeFor(Windows.Foundation.Size dips, double scale)
        => new((int)Math.Ceiling(dips.Width * scale), (int)Math.Ceiling(dips.Height * scale));

    /// <summary>On screen: shown, entering or leaving.</summary>
    public bool IsOnScreen => AppWindow.IsVisible;

    /// <summary>Size of the current surface (meaningful while on screen).</summary>
    public SizeInt32 Size => new(target.Width, target.Height);

    /// <summary>
    /// Enters at <paramref name="bounds"/>, or moves there if already shown (no new entrance), or turns back from a
    /// running exit. <paramref name="activate"/> gives the window focus (the panel); otherwise it never takes it.
    /// </summary>
    public void Show(RectInt32 bounds, double scale, bool activate)
    {
        frame = MeasureFrame();
        // A pending exit is cancelled; whatever was waiting for it (an action clicked in the panel) still runs
        var pending = hidden;
        hidden = null;
        pending?.Invoke();
        target = bounds;
        offset = Theme.MotionOffset * scale;
        wanted = true;
        var token = ++entry;
        if (AppWindow.IsVisible) Enter();
        else
        {
            // A hidden window does not lay out: shown as is, its first frame would still have the old content and
            // size. So it appears fully transparent and the entrance starts once XAML has drawn the new layout.
            reveal.Jump(false);
            Apply();
            if (activate) Window.Activate();
            else AppWindow.Show(activateWindow: false);
            Ui.AfterFrames(2, () => { if (token == entry && wanted) Enter(); });
        }
        if (activate) PInvoke.SetForegroundWindow(Hwnd); // from a second BioTap.exe the foreground lock can refuse Activate
    }

    void Enter()
    {
        if (SystemUi.AnimationsEnabled) reveal.Start(true, clock.ElapsedMilliseconds, (int)Theme.EnterDuration.TotalMilliseconds, (int)Theme.ExitDuration.TotalMilliseconds);
        else reveal.Jump(true);
        Apply();
        Run();
    }

    /// <summary>Leaves, then hides the window and calls <paramref name="done"/> (not called if shown again meanwhile).</summary>
    public void Hide(Action? done = null)
    {
        wanted = false;
        entry++;
        if (!AppWindow.IsVisible) { reveal.Jump(false); done?.Invoke(); return; }
        hidden = done;
        if (SystemUi.AnimationsEnabled) reveal.Start(false, clock.ElapsedMilliseconds, (int)Theme.EnterDuration.TotalMilliseconds, (int)Theme.ExitDuration.TotalMilliseconds);
        else reveal.Jump(false);
        Run();
    }

    void Run()
    {
        if (reveal.Running)
        {
            if (!ticking) { CompositionTarget.Rendering += OnFrame; ticking = true; }
            // Rendering can pause (locked session, covered window): this ends the transition anyway
            safety.Stop();
            safety.Interval = Theme.EnterDuration + TimeSpan.FromMilliseconds(100);
            safety.Start();
            return;
        }
        Stop();
        Finish();
    }

    void OnFrame(object? sender, object e) => Ui.Run(Frame, "glass frame");

    void Frame()
    {
        if (!ticking) return;
        reveal.Tick(clock.ElapsedMilliseconds);
        Apply();
        if (!reveal.Running) { Stop(); Finish(); }
    }

    void Stop()
    {
        safety.Stop();
        if (!ticking) return;
        CompositionTarget.Rendering -= OnFrame;
        ticking = false;
    }

    void Finish()
    {
        if (wanted) return;
        AppWindow.Hide();
        var done = hidden;
        hidden = null;
        done?.Invoke();
    }

    /// <summary>
    /// The invisible borders, measured on the window as it is (they depend on the style and the DPI). Not
    /// AppWindow.ResizeClient: it sizes as if the hidden caption were there, which left ~31 px of empty glass.
    /// </summary>
    (int, int, int, int) MeasureFrame()
    {
        var origin = new System.Drawing.Point(0, 0);
        PInvoke.ClientToScreen(Hwnd, ref origin);
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var client = AppWindow.ClientSize;
        int left = origin.X - position.X, top = origin.Y - position.Y;
        return (left, top, size.Width - client.Width - left, size.Height - client.Height - top);
    }

    void Apply()
    {
        var v = reveal.Value;
        var rect = new RectInt32(target.X - frame.Left, target.Y - frame.Top + (int)Math.Round((1 - v) * offset),
            target.Width + frame.Left + frame.Right, target.Height + frame.Top + frame.Bottom);
        if (rect.Width != applied.Width || rect.Height != applied.Height) AppWindow.MoveAndResize(rect);
        else if (rect.X != applied.X || rect.Y != applied.Y) AppWindow.Move(new PointInt32(rect.X, rect.Y));
        applied = rect;
        PInvoke.SetLayeredWindowAttributes(Hwnd, default, (byte)Math.Round(255 * v), LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }
}
