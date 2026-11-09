using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core.Tests;

public class AdminFolderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "biotap-adminfolder-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingFolder_IsCreated_WithOnlySystemAndAdmins_NotInherited()
    {
        var dir = Path.Combine(root, "BioTap");
        Directory.CreateDirectory(root);
        AclGuard.EnsureAdminOnlyFolder(dir);
        var sec = new DirectoryInfo(dir).GetAccessControl();
        Assert.True(sec.AreAccessRulesProtected); // nothing inherited from ProgramData (Users may create there)
        var sids = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).Distinct().Order();
        Assert.Equal(["S-1-5-18", "S-1-5-32-544"], sids);
    }

    [Fact]
    public void ExistingFolderWritableByUsers_IsRejected()
    {
        var dir = Path.Combine(root, "BioTap");
        Directory.CreateDirectory(dir); // inherits %TEMP% rights: the current (non-admin) user can write
        Assert.False(AclGuard.EnsureAdminOnlyFolder(dir));
    }

    public void Dispose()
    {
        // The test user still owns what it created, so it can give itself access back and clean up
        foreach (var d in Directory.Exists(root) ? Directory.GetDirectories(root) : [])
        {
            var sec = new DirectorySecurity();
            sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new DirectoryInfo(d).SetAccessControl(sec);
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
