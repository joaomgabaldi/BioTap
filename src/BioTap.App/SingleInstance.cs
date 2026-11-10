using System.Security.AccessControl;
using System.Security.Principal;
using BioTap.Core;

namespace BioTap.App;

/// <summary>
/// Single instance (spec §7). The authority is <see cref="InstanceLock"/>: a file held exclusively in the admin-only
/// ProgramData\BioTap folder, which a non-admin process cannot create or hold. A named event only carries the
/// "show panel" signal from a later BioTap.exe or from BioTapLauncher.exe (sem admin: o usuário só pode sinalizar);
/// it is used only if its ACL proves it is ours (named objects can be created first by any process).
/// </summary>
sealed class SingleInstance : IDisposable
{
    readonly InstanceLock instanceLock;
    readonly EventWaitHandle? showPanel;
    readonly ManualResetEvent stop = new(false);

    SingleInstance(InstanceLock instanceLock, EventWaitHandle? showPanel)
    {
        this.instanceLock = instanceLock;
        this.showPanel = showPanel;
    }

    /// <summary>Returns null when another BioTap is running (it was asked to show its panel).</summary>
    public static SingleInstance? TryStart(string dataFolder, FileLog log)
    {
        // Program.Main already made sure dataFolder exists and only admins can write it
        var lck = InstanceLock.TryAcquire(Path.Combine(dataFolder, "app.lock"));
        if (lck is null)
        {
            SignalRunningInstance();
            return null;
        }
        return new SingleInstance(lck, CreateOrRejectEvent(log));
    }

    static void SignalRunningInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowPanelEventAcl.Name);
            if (IsOurs(ev)) ev.Set();
        }
        catch (Exception)
        {
            // no trustworthy event: the running instance just does not open its panel
        }
    }

    static EventWaitHandle? CreateOrRejectEvent(FileLog log)
    {
        var security = new EventWaitHandleSecurity();
        security.SetSecurityDescriptorSddlForm(ShowPanelEventAcl.Sddl(WindowsIdentity.GetCurrent().User!));
        try
        {
            var ev = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowPanelEventAcl.Name, out var createdNew, security);
            if (createdNew || IsOurs(ev)) return ev;
            ev.Dispose();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
        }
        log.Write("WARN", "Show-panel event was created by another process; opening BioTap.exe again will not show the panel");
        return null;
    }

    /// <summary>Calls <paramref name="onShowPanel"/> for every later launch, including ones before this call (the event stays set).</summary>
    public void Listen(Action onShowPanel)
    {
        if (showPanel is null) return;
        new Thread(() =>
        {
            while (WaitHandle.WaitAny([showPanel, stop]) == 0) onShowPanel();
        }) { IsBackground = true, Name = "BioTap show-panel" }.Start();
    }

    static bool IsOurs(EventWaitHandle e)
    {
        try { return ShowPanelEventAcl.IsValid(e.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access), WindowsIdentity.GetCurrent().User!); }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        stop.Set();
        showPanel?.Dispose();
        instanceLock.Dispose();
    }
}
