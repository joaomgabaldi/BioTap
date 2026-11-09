using BioTap.Core.Actions;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace BioTap.App.Os;

/// <summary>IClipboardText through the WinRT clipboard, which needs the UI thread. The text is user data: never logged.</summary>
sealed class WinRtClipboard(DispatcherQueue ui) : IClipboardText
{
    public string? ReadText()
    {
        string? text = null;
        var done = new ManualResetEventSlim(); // never disposed: the queued work may still touch it after a timeout
        int state = 0; // 0 queued, 1 running, 2 abandoned by the caller
        void Work()
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
            try
            {
                var view = Clipboard.GetContent();
                if (view.Contains(StandardDataFormats.Text)) text = view.GetTextAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is not OutOfMemoryException) { text = null; } // no text, a locked clipboard, a failed read: all mean "nothing to show"
            finally { done.Set(); }
        }
        if (ui.HasThreadAccess) Work();
        else if (!ui.TryEnqueue(Work)) return null;
        // One 2 s budget in total. Not started in time: abandon it (it will do nothing). Already running: it is the only writer of text, so a late result is simply dropped
        if (done.Wait(TimeSpan.FromSeconds(2))) return Volatile.Read(ref text);
        Interlocked.CompareExchange(ref state, 2, 0);
        return null;
    }
}
