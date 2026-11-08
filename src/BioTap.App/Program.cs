using BioTap.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BioTap.App;

public static class Program
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BioTap");
    public static readonly FileLog Log = new(Path.Combine(DataFolder, "logs"), "app");

    [STAThread]
    static int Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("FATAL", $"Unhandled {e.ExceptionObject.GetType().Name}");

        using var instance = SingleInstance.TryStart(DataFolder, Log);
        if (instance is null) return 0; // another BioTap is running; it was asked to show the panel

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new App();
            instance.Listen(() => queue.TryEnqueue(app.RequestShowPanel));
        });
        return 0;
    }
}
