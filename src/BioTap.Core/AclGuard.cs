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

    static bool IsTrusted(IdentityReference? id) => id is SecurityIdentifier s && (s == SystemSid || s == Admins);

    public static bool IsWritableOnlyByAdmins(FileSystemSecurity sec)
    {
        if (!IsTrusted(sec.GetOwner(typeof(SecurityIdentifier)))) return false;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & WriteRights) != 0 && !IsTrusted(r.IdentityReference))
                return false;
        return true;
    }

    /// <summary>
    /// For named kernel objects (mutex, event): owner is SYSTEM/Administrators and every allow ACE is for one of them.
    /// Used to tell our own objects from ones a non-admin process created first under the same name (squatting).
    /// </summary>
    public static bool IsAdminOnly(string sddl)
    {
        try
        {
            var sd = new RawSecurityDescriptor(sddl);
            if (!IsTrusted(sd.Owner) || sd.DiscretionaryAcl is null) return false;
            foreach (var ace in sd.DiscretionaryAcl)
                if (ace is CommonAce { AceQualifier: AceQualifier.AccessAllowed } a && !IsTrusted(a.SecurityIdentifier)) return false;
            return true;
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
