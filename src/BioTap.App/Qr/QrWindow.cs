using BioTap.App.Shell;
using BioTap.Core.Actions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
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
    bool visible;

    public void Show(string text)
    {
        // Called from a pool thread; the QR is rendered here, off the UI thread
        var png = new PngByteQRCode(new QRCodeGenerator().CreateQrCode(text, QRCodeGenerator.ECCLevel.L)).GetGraphic(20);
        if (!ui.TryEnqueue(() => Ui.Run(() => ShowOnUi(png, text), "qr show"))) throw new PresetException("Não foi possível mostrar o QR code");
    }

    void ShowOnUi(byte[] png, string text)
    {
        if (window is null) Create();
        var stream = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(stream)) { w.WriteBytes(png); w.StoreAsync().AsTask().GetAwaiter().GetResult(); w.DetachStream(); }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        bitmap.SetSource(stream);
        image!.Source = bitmap;
        caption!.Text = text;

        var area = DisplayArea.GetFromPoint(Cursor(), DisplayAreaFallback.Primary);
        window!.AppWindow.Move(new PointInt32(area.WorkArea.X, area.WorkArea.Y)); // first on that monitor so the DPI scale is its own
        var scale = PInvoke.GetDpiForWindow(hwnd) / 96.0;
        int width = (int)((QrSize + Pad * 2) * scale), height = (int)((QrSize + TextHeight + Pad * 3) * scale);
        window.AppWindow.MoveAndResize(new RectInt32(
            area.WorkArea.X + (area.WorkArea.Width - width) / 2, area.WorkArea.Y + (area.WorkArea.Height - height) / 2, width, height));
        window.Activate(); // takes focus so Esc and click-outside close it
        PInvoke.SetForegroundWindow(hwnd);
        visible = true;
    }

    void Create()
    {
        image = new Image { Width = QrSize, Height = QrSize };
        caption = new TextBlock { MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Height = TextHeight };
        var root = new StackPanel { Padding = new Thickness(Pad), Spacing = Pad, Children = { image, caption } };
        root.KeyDown += (_, e) => Ui.Run(() => { if (e.Key == VirtualKey.Escape) Hide(); }, "qr key");
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
        window.Activated += (_, e) => Ui.Run(() => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(); }, "qr activated");
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    void Hide()
    {
        if (!visible) return;
        visible = false;
        window!.AppWindow.Hide();
    }

    static PointInt32 Cursor() => PInvoke.GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : new PointInt32(0, 0);
}
