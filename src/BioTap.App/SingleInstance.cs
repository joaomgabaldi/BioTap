using System.Security.AccessControl;
using System.Security.Principal;
using BioTap.Core;

namespace BioTap.App;

/// <summary>
/// Single instance (spec §7). The authority is <see cref="InstanceLock"/>: a file held exclusively in the admin-only
/// ProgramData\BioTap folder, which a non-admin process cannot create or hold. A named event only carries the
/// "show panel" signal from a later BioTap.exe; it is used only if its ACL proves it is ours (named objects can be
/// created first by any process).
/// </summary>
sealed class SingleInstance : IDisposable
{
    const string EventName = @"Local\BioTap.ShowPanel";

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
            using var ev = EventWaitHandle.OpenExisting(EventName);
            if (IsAdminOnly(ev)) ev.Set();
        }
        catch (Exception)
        {
            // no trustworthy event: the running instance just does not open its panel
        }
    }

    static EventWaitHandle? CreateOrRejectEvent(FileLog log)
    {
        var security = new EventWaitHandleSecurity();
        security.SetAccessRuleProtection(true, false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new EventWaitHandleAccessRule(admins, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        try
        {
            var ev = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, EventName, out var createdNew, security);
            if (createdNew || IsAdminOnly(ev)) return ev;
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

    static bool IsAdminOnly(EventWaitHandle e)
    {
        try { return AclGuard.IsAdminOnly(e.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access)); }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        stop.Set();
        showPanel?.Dispose();
        instanceLock.Dispose();
    }
}
