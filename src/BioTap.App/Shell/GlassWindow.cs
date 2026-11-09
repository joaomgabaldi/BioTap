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
/// </summary>
sealed class GlassWindow
{
    static readonly UISettings SystemUi = new();

    readonly Reveal reveal = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    RectInt32 target;
    double offset;
    RectInt32 applied;
    Action? hidden;
    bool ticking;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer safety;

    public Window Window { get; }
    public HWND Hwnd { get; }
    public AppWindow AppWindow => Window.AppWindow;

    /// <summary>Shown or entering (not leaving).</summary>
    public bool IsShown => reveal.Shown;

    /// <summary>Final on-screen bounds while shown or entering.</summary>
    public RectInt32? Bounds => reveal.Shown ? target : null;

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

    /// <summary>Outer size for a client area of <paramref name="client"/> physical pixels.</summary>
    public SizeInt32 OuterSizeFor(SizeInt32 client)
    {
        AppWindow.ResizeClient(client);
        return AppWindow.Size;
    }

    /// <summary>
    /// Enters at <paramref name="bounds"/>, or moves there if already shown (no new entrance), or turns back from a
    /// running exit. <paramref name="activate"/> gives the window focus (the panel); otherwise it never takes it.
    /// </summary>
    public void Show(RectInt32 bounds, double scale, bool activate)
    {
        // A pending exit is cancelled; whatever was waiting for it (an action clicked in the panel) still runs
        var pending = hidden;
        hidden = null;
        pending?.Invoke();
        target = bounds;
        offset = Theme.MotionOffset * scale;
        var wasVisible = AppWindow.IsVisible;
        if (SystemUi.AnimationsEnabled) reveal.Start(true, clock.ElapsedMilliseconds, (int)Theme.EnterDuration.TotalMilliseconds, (int)Theme.ExitDuration.TotalMilliseconds);
        else reveal.Jump(true);
        Apply();
        if (!wasVisible)
        {
            if (activate) Window.Activate();
            else AppWindow.Show(activateWindow: false);
        }
        if (activate) PInvoke.SetForegroundWindow(Hwnd); // from a second BioTap.exe the foreground lock can refuse Activate
        Run();
    }

    /// <summary>Leaves, then hides the window and calls <paramref name="done"/> (not called if shown again meanwhile).</summary>
    public void Hide(Action? done = null)
    {
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
        if (reveal.Shown) return;
        AppWindow.Hide();
        var done = hidden;
        hidden = null;
        done?.Invoke();
    }

    void Apply()
    {
        var v = reveal.Value;
        var rect = target with { Y = target.Y + (int)Math.Round((1 - v) * offset) };
        if (rect.Width != applied.Width || rect.Height != applied.Height) AppWindow.MoveAndResize(rect);
        else if (rect.X != applied.X || rect.Y != applied.Y) AppWindow.Move(new PointInt32(rect.X, rect.Y));
        applied = rect;
        PInvoke.SetLayeredWindowAttributes(Hwnd, default, (byte)Math.Round(255 * v), LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }
}
