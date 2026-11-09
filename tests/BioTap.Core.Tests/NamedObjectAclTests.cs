namespace BioTap.Core.Tests;

public class NamedObjectAclTests
{
    [Fact]
    public void AdminOwnedAdminOnly_IsTrusted()
        => Assert.True(AclGuard.IsAdminOnly("O:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)"));

    [Fact]
    public void UsersGrantedAnything_IsNotTrusted() // even just SYNCHRONIZE lets a user wait/signal tricks
        => Assert.False(AclGuard.IsAdminOnly("O:BAD:P(A;;GA;;;BA)(A;;0x100000;;;BU)"));

    [Fact]
    public void OwnedByAUser_IsNotTrusted()
        => Assert.False(AclGuard.IsAdminOnly("O:S-1-5-21-1-2-3-1001D:P(A;;GA;;;BA)"));

    [Fact]
    public void NullDacl_IsNotTrusted()
        => Assert.False(AclGuard.IsAdminOnly("O:BAD:NO_ACCESS_CONTROL"));

    [Fact]
    public void Malformed_IsNotTrusted()
        => Assert.False(AclGuard.IsAdminOnly("not an sddl"));
}
