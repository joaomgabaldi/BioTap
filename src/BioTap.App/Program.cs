using System.Security.Principal;
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
        // Fail closed before anything is written: this elevated process must not write where non-admins can redirect it
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return Fail("O BioTap precisa rodar como administrador.");
        if (!AclGuard.EnsureAdminOnlyFolder(DataFolder))
            return Fail($"A pasta {DataFolder} pode ser alterada por quem não é administrador. Reinstale o BioTap (install\\install.ps1) para corrigir.");

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("FATAL", $"Unhandled: {e.ExceptionObject}");

        using var instance = SingleInstance.TryStart(DataFolder, Log);
        if (instance is null) return 0; // another BioTap is running; it was asked to show the panel

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new App();
            instance.Listen(() => queue.TryEnqueue(() => BioTap.App.Shell.Ui.Run(app.RequestShowPanel, "show panel")));
        });
        return 0;
    }

    static int Fail(string message)
    {
        Windows.Win32.PInvoke.MessageBox(default, message, "BioTap", Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE.MB_ICONERROR);
        return 1;
    }
}
