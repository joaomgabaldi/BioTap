using System.Text.Json.Nodes;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Xunit;

namespace BioTap.Core.Tests;

public class QrCodePresetTests
{
    static (QrCodePreset P, FakeClipboardText C, FakeQrWindow W) Make(string? text)
    {
        var c = new FakeClipboardText { Text = text };
        var w = new FakeQrWindow();
        return (new QrCodePreset(c, w), c, w);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t")]
    public void No_text_is_not_ready(string? text) => Assert.Equal("Não há texto copiado", Make(text).P.CheckReady([]));

    [Fact]
    public void Over_2900_chars_is_not_ready() => Assert.Equal("O texto copiado passa de 2.900 caracteres", Make(new string('a', 2901)).P.CheckReady([]));

    [Fact]
    public void Exactly_2900_ascii_is_ready() => Assert.Null(Make(new string('a', 2900)).P.CheckReady([]));

    [Fact]
    public void Utf8_bytes_over_QR_capacity_is_not_ready() =>
        Assert.Equal("O texto copiado passa de 2.900 caracteres", Make(new string('é', 2900)).P.CheckReady([]));

    [Fact]
    public async Task Run_shows_the_exact_text()
    {
        var (p, _, w) = Make("  olá\nmundo ");
        await p.RunAsync([], CancellationToken.None);
        Assert.Equal(["  olá\nmundo "], w.Shown);
    }

    [Fact]
    public async Task Run_throws_when_clipboard_became_empty()
    {
        var (p, _, w) = Make(null);
        await Assert.ThrowsAsync<PresetException>(() => p.RunAsync([], CancellationToken.None));
        Assert.Empty(w.Shown);
    }

    [Fact]
    public void Metadata_and_params()
    {
        var (p, _, _) = Make("x");
        Assert.Equal("qrCode", p.Id);
        Assert.Equal("QR code do texto copiado", p.DisplayName);
        Assert.Equal(new IconRef("glyph", "ED14"), p.DefaultIcon);
        Assert.Null(p.ValidateParams(new JsonObject()));
    }
}
