using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BioTap.Core;

/// <summary>
/// "Iniciar com o Windows" (spec §4/§6): a scheduled task at the current user's logon, run with highest
/// privileges, so the admin app starts without a UAC prompt. Task Scheduler COM API, late-bound (no command line).
/// </summary>
public sealed class StartupTask(string taskName = "BioTap")
{
    const int TriggerLogon = 9, ActionExec = 0, CreateOrUpdate = 6, LogonInteractiveToken = 3, RunLevelHighest = 1;

    public bool IsEnabled() => Use(folder =>
    {
        try { Release(((dynamic)folder).GetTask(taskName)); return true; }
        catch (Exception e) when (e is COMException or FileNotFoundException) { return false; }
    });

    /// <summary>
    /// Refuses an exe that a non-admin could replace (e.g. a build folder or Downloads): a logon task with highest
    /// privileges for such a file would hand admin rights to whoever writes it.
    /// </summary>
    public void Enable(string exePath)
    {
        if (!AclGuard.IsWritableOnlyByAdmins(exePath))
            throw new InvalidOperationException("O BioTap precisa estar instalado em uma pasta só de administradores (Program Files).");
        Register(exePath);
    }

    void Register(string exePath) => Use(folder =>
    {
        object? service = null;
        try
        {
            service = Connect();
            dynamic def = ((dynamic)service).NewTask(0);
            var user = WindowsIdentity.GetCurrent().Name;
            def.RegistrationInfo.Description = "Inicia o BioTap ao entrar no Windows.";
            def.Principal.UserId = user;
            def.Principal.LogonType = LogonInteractiveToken;
            def.Principal.RunLevel = RunLevelHighest;
            def.Settings.ExecutionTimeLimit = "PT0S";
            def.Settings.DisallowStartIfOnBatteries = false;
            def.Settings.StopIfGoingOnBatteries = false;
            dynamic trigger = def.Triggers.Create(TriggerLogon);
            trigger.UserId = user;
            dynamic action = def.Actions.Create(ActionExec);
            action.Path = exePath;
            action.WorkingDirectory = Path.GetDirectoryName(exePath);
            Release(((dynamic)folder).RegisterTaskDefinition(taskName, def, CreateOrUpdate, null, null, LogonInteractiveToken));
            return true;
        }
        finally { Release(service); }
    });

    public void Disable() => Use(folder =>
    {
        try { ((dynamic)folder).DeleteTask(taskName, 0); }
        catch (Exception e) when (e is COMException or FileNotFoundException) { } // already gone
        return true;
    });

    static object Connect()
    {
        var service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        ((dynamic)service).Connect();
        return service;
    }

    static T Use<T>(Func<object, T> body)
    {
        object? service = null, folder = null;
        try
        {
            service = Connect();
            folder = ((dynamic)service).GetFolder("\\");
            return body(folder!);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
    }
}
