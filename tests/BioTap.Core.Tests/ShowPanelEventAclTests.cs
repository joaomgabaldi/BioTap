using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core.Tests;

/// <summary>Spec do launcher §3.3/§6: o evento do painel só vale se for nosso; o usuário só pode sinalizar e ler.</summary>
public class ShowPanelEventAclTests
{
    static readonly SecurityIdentifier User = new("S-1-5-21-1-2-3-1001");
    const string Other = "S-1-5-21-1-2-3-1002";

    [Fact]
    public void Sddl_IsValidForItsUser()
        => Assert.True(ShowPanelEventAcl.IsValid(ShowPanelEventAcl.Sddl(User), User));

    [Fact]
    public void Sddl_GivesUserExactlySignalAndRead()
    {
        var sd = new RawSecurityDescriptor(ShowPanelEventAcl.Sddl(User));
        var ace = Assert.Single(sd.DiscretionaryAcl!.Cast<CommonAce>(), a => a.SecurityIdentifier == User);
        Assert.Equal(0x20002, ace.AccessMask);
    }

    [Fact]
    public void IsValid_AcceptsAdminOnly()
        => Assert.True(ShowPanelEventAcl.IsValid("O:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)", User));

    [Fact]
    public void IsValid_AcceptsUserWithSignalOnly()
        => Assert.True(ShowPanelEventAcl.IsValid($"O:BAD:P(A;;GA;;;BA)(A;;0x2;;;{User})", User));

    [Fact]
    public void IsValid_RejectsUserOwner() // squat: um processo sem admin não consegue pôr Administradores como dono
        => Assert.False(ShowPanelEventAcl.IsValid($"O:{User}D:P(A;;GA;;;BA)", User));

    [Theory]
    [InlineData("0x40000")]   // WRITE_DAC
    [InlineData("0x100000")]  // SYNCHRONIZE
    [InlineData("0x1f0003")]  // EVENT_ALL_ACCESS
    [InlineData("GA")]
    public void IsValid_RejectsUserWithExtraRights(string mask)
        => Assert.False(ShowPanelEventAcl.IsValid($"O:BAD:P(A;;GA;;;BA)(A;;{mask};;;{User})", User));

    [Fact]
    public void IsValid_RejectsOtherUser()
        => Assert.False(ShowPanelEventAcl.IsValid($"O:BAD:P(A;;GA;;;BA)(A;;0x20002;;;{Other})", User));

    [Fact]
    public void IsValid_RejectsEveryone()
        => Assert.False(ShowPanelEventAcl.IsValid("O:BAD:P(A;;GA;;;BA)(A;;0x2;;;WD)", User));

    [Fact]
    public void IsValid_RejectsNullDacl()
        => Assert.False(ShowPanelEventAcl.IsValid("O:BAD:NO_ACCESS_CONTROL", User));

    [Fact]
    public void IsValid_RejectsGarbage()
        => Assert.False(ShowPanelEventAcl.IsValid("not an sddl", User));

    [Fact]
    public void IsValid_RejectsOtherAllowAceTypes() // object ACE (OA) também concede acesso
        => Assert.False(ShowPanelEventAcl.IsValid("O:BAD:P(A;;GA;;;BA)(OA;;GA;;;WD)", User));

    [Fact]
    public void IsValid_AcceptsDenyAce()
        => Assert.True(ShowPanelEventAcl.IsValid("O:BAD:P(D;;GA;;;WD)(A;;GA;;;BA)", User));
}
