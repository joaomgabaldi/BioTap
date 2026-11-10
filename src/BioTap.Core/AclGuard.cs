using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core;

/// <summary>Checks that only SYSTEM and Administrators can change a file (spec §5).</summary>
public static class AclGuard
{
    static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership |
        FileSystemRights.DeleteSubdirectoriesAndFiles | // on a folder: delete a child and replace it
        (FileSystemRights)0x40000000 | // GENERIC_WRITE
        (FileSystemRights)0x10000000;  // GENERIC_ALL

    // Owns Windows' own files; more privileged than an admin, so it is trusted too
    static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    static bool IsTrusted(IdentityReference? id)
        => id is SecurityIdentifier s && (s == SystemSid || s == Admins || s == TrustedInstaller);

    public static bool IsWritableOnlyByAdmins(FileSystemSecurity sec)
    {
        if (!IsTrusted(sec.GetOwner(typeof(SecurityIdentifier)))) return false;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            // An inherit-only ACE (e.g. CREATOR OWNER on a system folder) applies to new children, not to this object
            if (r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if (r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & WriteRights) != 0 && !IsTrusted(r.IdentityReference))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The app's data folder (lock, logs, config). Created missing with only SYSTEM + Administrators, nothing inherited
    /// (ProgramData lets Users create subfolders). Fails closed: an existing folder others can write, a reparse point
    /// or an unreadable ACL returns false.
    /// </summary>
    public static bool EnsureAdminOnlyFolder(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                foreach (var sid in new[] { SystemSid, Admins })
                    sec.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                dir.Create(sec);
                dir.Refresh();
            }
            return !dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && IsWritableOnlyByAdmins(dir.GetAccessControl());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The file and the folder holding it (whoever can replace the file controls it too).</summary>
    public static bool IsSecure(FileSystemSecurity file, FileSystemSecurity folder)
        => IsWritableOnlyByAdmins(file) && IsWritableOnlyByAdmins(folder);

    /// <summary>Checks the file and its folder. Fails closed: missing, reparse point or unreadable ACL count as insecure.</summary>
    public static bool IsWritableOnlyByAdmins(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            var folder = file.Directory!;
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            return IsSecure(file.GetAccessControl(), folder.GetAccessControl());
        }
        catch (Exception)
        {
            return false; // fail closed on any error reading the ACL
        }
    }
}
