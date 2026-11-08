using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace BioTap.App;

public static class Program
{
    [STAThread]
    static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey("BioTap");
        if (!instance.IsCurrent)
        {
            // Second launch: only ask the running instance to show the panel. Its arguments are ignored (spec §7).
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(args).AsTask()).Wait();
            return 0;
        }

        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new App();
            instance.Activated += (_, _) => queue.TryEnqueue(app.RequestShowPanel);
        });
        return 0;
    }
}
