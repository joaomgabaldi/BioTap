using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core.Tests;

public class AclGuardTests
{
    static readonly SecurityIdentifier SystemSid = new("S-1-5-18");
    static readonly SecurityIdentifier Admins = new("S-1-5-32-544");
    static readonly SecurityIdentifier Users = new("S-1-5-32-545");
    static readonly SecurityIdentifier AuthUsers = new("S-1-5-11");

    static FileSecurity Secured(SecurityIdentifier? owner = null)
    {
        var s = new FileSecurity();
        s.SetAccessRuleProtection(true, false);
        s.SetOwner(owner ?? Admins);
        s.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
        return s;
    }

    [Fact]
    public void OnlySystemAndAdmins_IsSecure() => Assert.True(AclGuard.IsWritableOnlyByAdmins(Secured()));

    [Fact]
    public void UsersWrite_IsInsecure()
    {
        var s = Secured();
        s.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Write, AccessControlType.Allow));
        Assert.False(AclGuard.IsWritableOnlyByAdmins(s));
    }

    [Fact]
    public void AuthenticatedUsersModify_IsInsecure()
    {
        var s = Secured();
        s.AddAccessRule(new FileSystemAccessRule(AuthUsers, FileSystemRights.Modify, AccessControlType.Allow));
        Assert.False(AclGuard.IsWritableOnlyByAdmins(s));
    }

    [Theory]
    [InlineData(FileSystemRights.AppendData)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.TakeOwnership)]
    [InlineData(FileSystemRights.WriteAttributes)]
    [InlineData(FileSystemRights.WriteExtendedAttributes)]
    public void UsersAnySingleWriteRight_IsInsecure(FileSystemRights right)
    {
        var s = Secured();
        s.AddAccessRule(new FileSystemAccessRule(Users, right, AccessControlType.Allow));
        Assert.False(AclGuard.IsWritableOnlyByAdmins(s));
    }

    [Theory]
    [InlineData(0x40000000)] // GENERIC_WRITE
    [InlineData(0x10000000)] // GENERIC_ALL
    public void UsersGenericWriteRights_IsInsecure(int right)
    {
        var s = Secured();
        var sd = new RawSecurityDescriptor(s.GetSecurityDescriptorSddlForm(AccessControlSections.All));
        sd.DiscretionaryAcl!.InsertAce(sd.DiscretionaryAcl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, right, Users, false, null));
        var withGeneric = new FileSecurity();
        withGeneric.SetSecurityDescriptorSddlForm(sd.GetSddlForm(AccessControlSections.All));
        Assert.False(AclGuard.IsWritableOnlyByAdmins(withGeneric));
    }

    [Fact]
    public void UsersReadOnly_IsSecure()
    {
        var s = Secured();
        s.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        Assert.True(AclGuard.IsWritableOnlyByAdmins(s));
    }

    [Fact]
    public void OwnerNotAdmin_IsInsecure()
        => Assert.False(AclGuard.IsWritableOnlyByAdmins(Secured(WindowsIdentity.GetCurrent().User!)));

    [Fact]
    public void DenyRuleForUsers_StillSecure()
    {
        var s = Secured();
        s.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Write, AccessControlType.Deny));
        Assert.True(AclGuard.IsWritableOnlyByAdmins(s));
    }

    [Fact]
    public void Path_MissingFile_IsInsecure()
        => Assert.False(AclGuard.IsWritableOnlyByAdmins(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));

    [Fact]
    public void Path_UserTempFile_IsInsecure()
    {
        var p = Path.GetTempFileName();
        try { Assert.False(AclGuard.IsWritableOnlyByAdmins(p)); }
        finally { File.Delete(p); }
    }
}

public class AclGuardFolderTests
{
    static readonly SecurityIdentifier Admins = new("S-1-5-32-544");
    static readonly SecurityIdentifier SystemSid = new("S-1-5-18");
    static readonly SecurityIdentifier Users = new("S-1-5-32-545");

    static T Secured<T>(T s) where T : FileSystemSecurity
    {
        s.SetAccessRuleProtection(true, false);
        s.SetOwner(Admins);
        s.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
        return s;
    }

    [Fact]
    public void SecureFileInSecureFolder_IsSecure()
        => Assert.True(AclGuard.IsSecure(Secured(new FileSecurity()), Secured(new DirectorySecurity())));

    [Fact]
    public void SecureFileInFolderUsersCanDeleteFrom_IsInsecure()
    {
        // With "delete child" on the folder a user can delete config.json and drop in their own.
        var dir = Secured(new DirectorySecurity());
        dir.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Allow));
        Assert.False(AclGuard.IsSecure(Secured(new FileSecurity()), dir));
    }

    [Fact]
    public void SecureFileInFolderUsersCanCreateFilesIn_IsInsecure()
    {
        var dir = Secured(new DirectorySecurity());
        dir.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.CreateFiles, AccessControlType.Allow));
        Assert.False(AclGuard.IsSecure(Secured(new FileSecurity()), dir));
    }
}
