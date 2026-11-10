using BioTap.Core.Text;

namespace BioTap.Core.Tests;

/// <summary>Spec da tradução §3: config → instalador (HKLM) → idioma do Windows.</summary>
public class LanguageChoiceTests
{
    [Fact]
    public void Config_Wins()
        => Assert.Equal(Language.En, LanguageChoice.Resolve("en", "pt-BR", "pt-BR"));

    [Fact]
    public void Installer_WhenNoConfig()
        => Assert.Equal(Language.En, LanguageChoice.Resolve(null, "en", "pt-BR"));

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("pt")]
    public void Windows_Pt_IsPtBr(string ui)
        => Assert.Equal(Language.PtBr, LanguageChoice.Resolve(null, null, ui));

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    [InlineData("")]
    public void Windows_Other_IsEn(string ui)
        => Assert.Equal(Language.En, LanguageChoice.Resolve(null, null, ui));

    public static TheoryData<string> Invalid => new() { "EN", "pt", "", " en", "en\n", "PT-BR", new string('x', 5000) };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_FallsThrough(string bad)
    {
        Assert.Equal(Language.PtBr, LanguageChoice.Resolve(bad, null, "pt-BR"));
        Assert.Equal(Language.En, LanguageChoice.Resolve(bad, "en", "pt-BR"));
        Assert.Equal(Language.PtBr, LanguageChoice.Resolve(null, bad, "pt-BR"));
    }

    [Theory]
    [InlineData(Language.PtBr, "pt-BR")]
    [InlineData(Language.En, "en")]
    public void Code_RoundTrips(Language l, string code)
    {
        Assert.Equal(code, LanguageChoice.Code(l));
        Assert.Equal(l, LanguageChoice.Parse(code));
    }
}
