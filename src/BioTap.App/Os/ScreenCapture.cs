using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BioTap.Core.Actions;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;

namespace BioTap.App.Os;

/// <summary>IScreenCapture through GDI (pixels) and the WinRT clipboard (which needs the UI thread). Never logs image data.</summary>
sealed class ScreenCapture(DispatcherQueue ui) : IScreenCapture
{
    public byte[] CaptureMonitorUnderMouse()
    {
        try
        {
            PInvoke.GetCursorPos(out var cursor);
            var mon = PInvoke.MonitorFromPoint(cursor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!PInvoke.GetMonitorInfo(mon, ref mi)) throw new InvalidOperationException();
            return Grab(mi.rcMonitor) ?? throw new InvalidOperationException();
        }
        catch (Exception e) when (e is not PresetException)
        {
            throw new PresetException("Não foi possível capturar a tela");
        }
    }

    public byte[]? CaptureWindow(nint window)
    {
        try
        {
            var h = (HWND)window;
            if (!PInvoke.IsWindow(h)) return null;
            RECT frame = default;
            // The visible frame, without the invisible resize border Windows 10/11 adds around a window
            if (PInvoke.DwmGetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref frame, 1))).Failed) return null;
            return Grab(frame);
        }
        catch (Exception e) when (e is not PresetException)
        {
            throw new PresetException("Não foi possível capturar a tela");
        }
    }

    static byte[]? Grab(RECT r)
    {
        int w = r.right - r.left, h = r.bottom - r.top;
        if (w <= 0 || h <= 0) return null;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.left, r.top, 0, 0, new Size(w, h));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public void CopyImage(byte[] png)
    {
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        void Work()
        {
            try
            {
                var stream = new InMemoryRandomAccessStream();
                using (var w = new DataWriter(stream)) { w.WriteBytes(png); w.StoreAsync().AsTask().GetAwaiter().GetResult(); w.DetachStream(); }
                stream.Seek(0);
                var package = new DataPackage();
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        }
        if (ui.HasThreadAccess) Work();
        else if (!ui.TryEnqueue(Work)) throw new PresetException("Não foi possível copiar a imagem");
        if (!done.Wait(TimeSpan.FromSeconds(2)) || error is not null) throw new PresetException("Não foi possível copiar a imagem");
    }
}
