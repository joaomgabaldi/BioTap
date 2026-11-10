using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using BioTap.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.Launcher;

/// <summary>
/// Atalho do BioTap sem UAC (spec do launcher §4): com o app aberto, só pede o painel pelo evento; com o app fechado,
/// abre o BioTap.exe, que pede UAC. Não lê a linha de comando nem variáveis de ambiente.
/// </summary>
static class Program
{
    const int ErrorCancelled = 1223; // o usuário recusou o UAC

    static int Main()
    {
        if (SignalRunningApp()) return 0;
        try
        {
            var exe = AppPath();
            using var app = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe) });
            return 0;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return 0;
        }
        catch (Exception)
        {
            PInvoke.MessageBox(HWND.Null, "Não foi possível abrir o BioTap.", "BioTap", MESSAGEBOX_STYLE.MB_ICONERROR);
            return 1;
        }
    }

    /// <summary>True só se o evento existe, é do app elevado (ACL conferida) e foi sinalizado.</summary>
    static bool SignalRunningApp()
    {
        try
        {
            using var ev = EventWaitHandleAcl.OpenExisting(ShowPanelEventAcl.Name, (EventWaitHandleRights)ShowPanelEventAcl.UserRights);
            var sddl = ev.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
            return ShowPanelEventAcl.IsValid(sddl, WindowsIdentity.GetCurrent().User!) && ev.Set();
        }
        catch (Exception)
        {
            return false; // não existe ou acesso negado: o app está fechado (ou o evento não é nosso)
        }
    }

    /// <summary>Known Folder do Program Files (não a variável de ambiente, que o usuário controla). Caminho relativo nunca.</summary>
    static string AppPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify);
        if (!Path.IsPathFullyQualified(programFiles)) throw new InvalidOperationException("Program Files não encontrado.");
        return Path.Combine(programFiles, "BioTap", "App", "BioTap.exe");
    }
}
