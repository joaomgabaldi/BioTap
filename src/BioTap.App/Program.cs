using System.Security.Principal;
using BioTap.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using BioTap.Core.Text;

namespace BioTap.App;

public static class Program
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BioTap");
    public static readonly FileLog Log = new(Path.Combine(DataFolder, "logs"), "app");

    /// <summary>Idioma gravado pelo instalador (spec da tradução §3). Só HKLM: um processo sem admin não o altera.</summary>
    public static string? InstallerLanguage()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\BioTap");
            return key?.GetValue("Language") as string;
        }
        catch (Exception)
        {
            return null; // sem permissão ou chave corrompida: cai no idioma do Windows
        }
    }

    /// <summary>Idioma em uso: o das preferências (config.json), senão o do instalador, senão o do Windows.</summary>
    public static void ApplyLanguage(string? fromConfig)
        => Strings.Current = Strings.For(LanguageChoice.Resolve(fromConfig, InstallerLanguage(), System.Globalization.CultureInfo.CurrentUICulture.Name));

    [STAThread]
    static int Main()
    {
        ApplyLanguage(null); // as mensagens de erro abaixo vêm antes de ler a configuração
        // Fail closed before anything is written: this elevated process must not write where non-admins can redirect it
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return Fail(Strings.Current.NeedsAdmin);
        if (!AclGuard.EnsureAdminOnlyFolder(DataFolder))
            return Fail(Strings.Current.DataFolderInsecure(DataFolder));

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
