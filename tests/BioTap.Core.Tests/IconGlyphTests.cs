using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public class IconGlyphTests
{
    [Theory]
    [InlineData("E8E5", "")]
    [InlineData("1F600", "\U0001F600")]
    public void ValidCodePoint_BecomesItsText(string hex, string expected)
        => Assert.Equal(expected, new IconRef("glyph", hex).GlyphText());

    [Theory]
    [InlineData("D800")]      // surrogate: ConvertFromUtf32 throws
    [InlineData("DFFF")]
    [InlineData("110000")]    // above Unicode
    [InlineData("FFFFFFFF")]  // parses as -1
    [InlineData("1F")]        // control character
    [InlineData("zz")]
    [InlineData("")]
    public void InvalidCodePoint_IsEmpty_NeverThrows(string hex)
        => Assert.Equal("", new IconRef("glyph", hex).GlyphText());

    [Fact]
    public void OtherKinds_AreEmpty() => Assert.Equal("", new IconRef("exe", "E8E5").GlyphText());
}
