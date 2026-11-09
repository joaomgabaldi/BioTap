using BioTap.App.Shell;
using BioTap.Core.Actions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace BioTap.App.Qr;

/// <summary>
/// IQrWindow: one reused, borderless, always-on-top window with the QR code and the text (at most 2 lines) below;
/// centered on the monitor under the mouse; closes on Esc or when it loses focus. The text is never logged.
/// </summary>
sealed class QrWindow(DispatcherQueue ui) : IQrWindow
{
    const int QrSize = 320, Pad = 16, TextHeight = 44;
    Window? window;
    Image? image;
    TextBlock? caption;
    HWND hwnd;
    bool visible, clickCloses;
    DispatcherQueueTimer? timer;

    public Task ShowAsync(string text, CancellationToken abandon)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int state = 0; // 0 queued, 1 running, 2 abandoned by the caller (it already reported failure: show nothing late)
        abandon.Register(() => Interlocked.CompareExchange(ref state, 2, 0));
        var queued = ui.TryEnqueue(() => Ui.Run(() =>
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
            try { ShowOnUi(text); done.TrySetResult(); }
            catch (Exception e) { done.TrySetException(e); throw; } // Ui.Run still logs it
        }, "qr show"));
        if (!queued) done.TrySetException(new InvalidOperationException("ui queue closed"));
        return done.Task;
    }

    /// <summary>
    /// PNG of the QR (ECC L, black on white, quiet zone) with whole pixels per module, about <paramref name="targetPixels"/> wide.
    /// Forced UTF-8 without BOM and without an ECI header (which costs capacity). Not unit-tested: QRCoder is App-only and the App has no test project.
    /// </summary>
    internal static (byte[] Png, int Pixels) Render(string text, int targetPixels)
    {
        var data = new QRCodeGenerator().CreateQrCode(text, QRCodeGenerator.ECCLevel.L, forceUtf8: true, utf8BOM: false);
        int ppm = Math.Max(1, targetPixels / data.ModuleMatrix.Count);
        return (new PngByteQRCode(data).GetGraphic(ppm), ppm * data.ModuleMatrix.Count);
    }

    void ShowOnUi(string text)
    {
        if (window is null) Create();
        var area = DisplayArea.GetFromPoint(Cursor(), DisplayAreaFallback.Primary);
        window!.AppWindow.Move(new PointInt32(area.WorkArea.X, area.WorkArea.Y)); // first on that monitor so the DPI scale is its own
        var scale = PInvoke.GetDpiForWindow(hwnd) / 96.0;

        var (png, pixels) = Render(text, (int)(QrSize * scale));
        var stream = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(stream)) { w.WriteBytes(png); w.StoreAsync().AsTask().GetAwaiter().GetResult(); w.DetachStream(); }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        bitmap.SetSource(stream);
        image!.Source = bitmap;
        image.Width = image.Height = pixels / scale; // whole pixels per module
        caption!.Text = text;

        int width = (int)((QrSize + Pad * 2) * scale), height = (int)((QrSize + TextHeight + Pad * 3) * scale);
        window.AppWindow.MoveAndResize(new RectInt32(
            area.WorkArea.X + (area.WorkArea.Width - width) / 2, area.WorkArea.Y + (area.WorkArea.Height - height) / 2, width, height));
        visible = true; // before Activate: its Deactivated event must be able to hide the window
        window.Activate(); // takes focus so Esc and click-outside close it
        clickCloses = !Focus();
        timer!.Stop();
        timer.Start(); // restarted on each Show
    }

    /// <summary>SetForegroundWindow, then (if refused) once more attached to the foreground window's input thread.</summary>
    bool Focus()
    {
        if (PInvoke.SetForegroundWindow(hwnd)) return true;
        var fg = PInvoke.GetForegroundWindow();
        if (fg == HWND.Null) return false;
        var other = PInvoke.GetWindowThreadProcessId(fg, out _);
        var mine = PInvoke.GetCurrentThreadId();
        if (other == 0 || other == mine || !PInvoke.AttachThreadInput(mine, other, true)) return false;
        try { return PInvoke.SetForegroundWindow(hwnd); }
        finally { PInvoke.AttachThreadInput(mine, other, false); }
    }

    void Create()
    {
        image = new Image { Stretch = Stretch.None };
        caption = new TextBlock { MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Height = TextHeight };
        var root = new StackPanel { Padding = new Thickness(Pad), Spacing = Pad, Background = new SolidColorBrush(Colors.Transparent), Children = { image, caption } };
        var esc = new KeyboardAccelerator { Key = VirtualKey.Escape };
        esc.Invoked += (_, e) => Ui.Run(() => { e.Handled = true; Hide(); }, "qr key");
        root.KeyboardAccelerators.Add(esc);
        // Without focus (foreground lock) Esc and click-outside cannot work: then a click on the window closes it
        root.PointerPressed += (_, _) => Ui.Run(() => { if (clickCloses) Hide(); }, "qr click");
        window = new Window { SystemBackdrop = new ActiveAcrylicBackdrop(), Content = root };
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        window.AppWindow.IsShownInSwitchers = false;
        // Alt+F4 / system close must hide, not destroy: the window is reused and the preset would otherwise fail forever
        window.AppWindow.Closing += (_, e) => Ui.Run(() => { e.Cancel = true; Hide(); }, "qr closing");
        window.Activated += (_, e) => Ui.Run(() => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(); }, "qr activated");
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        timer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Ui.Run(Hide, "qr timeout");
    }

    void Hide()
    {
        if (!visible) return;
        visible = false;
        timer?.Stop();
        window!.AppWindow.Hide();
    }

    static PointInt32 Cursor() => PInvoke.GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : new PointInt32(0, 0);
}
