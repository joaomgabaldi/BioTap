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
        FileSystemRights.TakeOwnership;

    static bool IsTrusted(IdentityReference? id) => id is SecurityIdentifier s && (s == SystemSid || s == Admins);

    public static bool IsWritableOnlyByAdmins(FileSystemSecurity sec)
    {
        if (!IsTrusted(sec.GetOwner(typeof(SecurityIdentifier)))) return false;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & WriteRights) != 0 && !IsTrusted(r.IdentityReference))
                return false;
        return true;
    }

    /// <summary>Fails closed: missing file, reparse point or unreadable ACL count as insecure.</summary>
    public static bool IsWritableOnlyByAdmins(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            return IsWritableOnlyByAdmins(file.GetAccessControl());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
