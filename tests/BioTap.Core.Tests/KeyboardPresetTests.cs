using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

public sealed class KeyboardPresetTests
{
    readonly FakeKeyboard kb = new();

    static string Chord(params ushort[] keys) => "Chord(" + string.Join(",", keys.Select(k => "0x" + k.ToString("X2"))) + ")";
    static string Tap(ushort key, int times) => $"Tap(0x{key:X2},{times})";

    [Fact]
    public async Task Shortcut_WinAltR_SendsChordInOrder()
    {
        await new ShortcutPreset(kb).RunAsync(new JsonObject { ["win"] = true, ["alt"] = true, ["key"] = "R" }, default);
        Assert.Equal([Chord(Vk.Menu, Vk.LWin, 0x52)], kb.Calls);
    }

    [Fact]
    public async Task Shortcut_AllModifiers_CtrlShiftAltWinThenKey()
    {
        await new ShortcutPreset(kb).RunAsync(new JsonObject { ["win"] = true, ["alt"] = true, ["shift"] = true, ["ctrl"] = true, ["key"] = "F5" }, default);
        Assert.Equal([Chord(Vk.Control, Vk.Shift, Vk.Menu, Vk.LWin, 0x74)], kb.Calls);
    }

    [Theory]
    [InlineData(null, "Escolha a tecla.")]
    [InlineData("Ç", "Tecla inválida.")]
    [InlineData("F5", null)]
    public void Shortcut_Validate_Key(string? key, string? expected)
    {
        var p = new JsonObject();
        if (key is not null) p["key"] = key;
        Assert.Equal(expected, new ShortcutPreset(kb).ValidateParams(p));
    }

    [Fact]
    public void SystemFeedback_OnlyVolumeAndMute()
    {
        IPreset[] own = [new KeyPreset("mute", "m", "E74F", kb, Vk.VolumeMute), new VolumeStepPreset(kb, up: true), new VolumeStepPreset(kb, up: false)];
        IPreset[] pill = [new KeyPreset("playPause", "p", "E768", kb, Vk.MediaPlayPause), new KeyPreset("copy", "c", "E8C8", kb, Vk.Control, 0x43), new ShortcutPreset(kb)];
        Assert.All(own, p => Assert.True(p.SystemFeedback));
        Assert.All(pill, p => Assert.False(p.SystemFeedback));
    }

    [Fact]
    public void Shortcut_Validate_WinL_Refused()
        => Assert.Equal("O Windows não deixa enviar Win+L. Use o preset Bloquear o Windows.",
            new ShortcutPreset(kb).ValidateParams(new JsonObject { ["win"] = true, ["key"] = "L" }));

    [Fact]
    public void Shortcut_Validate_WinShiftL_Allowed()
        => Assert.Null(new ShortcutPreset(kb).ValidateParams(new JsonObject { ["win"] = true, ["shift"] = true, ["key"] = "L" }));

    [Fact]
    public void Shortcut_Validate_CtrlAltDel_Refused()
        => Assert.Equal("O Windows não deixa enviar Ctrl+Alt+Del.",
            new ShortcutPreset(kb).ValidateParams(new JsonObject { ["ctrl"] = true, ["alt"] = true, ["key"] = "Delete" }));

    [Fact]
    public async Task VolumeStep_TapsHalfTheStep()
    {
        await new VolumeStepPreset(kb, up: true).RunAsync(new JsonObject { ["step"] = 10 }, default);
        await new VolumeStepPreset(kb, up: false).RunAsync(new JsonObject { ["step"] = 50 }, default);
        Assert.Equal([Tap(Vk.VolumeUp, 5), Tap(Vk.VolumeDown, 25)], kb.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(3)]
    public void VolumeStep_Validate_Invalid(int step)
        => Assert.Equal("Escolha um passo par de 2 a 50%.", new VolumeStepPreset(kb, true).ValidateParams(new JsonObject { ["step"] = step }));

    [Fact]
    public void VolumeStep_Validate_MissingOrString()
    {
        Assert.Equal("Escolha um passo par de 2 a 50%.", new VolumeStepPreset(kb, true).ValidateParams(new JsonObject()));
        Assert.Equal("Escolha um passo par de 2 a 50%.", new VolumeStepPreset(kb, true).ValidateParams(new JsonObject { ["step"] = "10" }));
        Assert.Equal("Escolha um passo par de 2 a 50%.", new VolumeStepPreset(kb, true).ValidateParams(new JsonObject { ["step"] = 10.5 }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public void VolumeStep_Validate_Ok(int step)
        => Assert.Null(new VolumeStepPreset(kb, false).ValidateParams(new JsonObject { ["step"] = step }));

    [Theory]
    [InlineData("olá 👍\r\nfim", "olá 👍\nfim")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\nb", "a\nb")]
    public async Task TypeText_NormalizesNewlines_KeepsEverythingElse(string text, string expected)
    {
        await new TypeTextPreset(kb).RunAsync(new JsonObject { ["text"] = text }, default);
        Assert.Equal(["Type:" + expected], kb.Calls);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData(1)]
    public void Shortcut_NonBoolModifier_IsInvalid(object value)
    {
        var node = value is string s ? JsonValue.Create(s) : JsonValue.Create((int)value);
        Assert.Equal("Atalho inválido.", new ShortcutPreset(kb).ValidateParams(new JsonObject { ["ctrl"] = node, ["key"] = "C" }));
    }

    [Fact]
    public void TypeText_Validate()
    {
        var preset = new TypeTextPreset(kb);
        Assert.Equal("Escreva o texto.", preset.ValidateParams(new JsonObject { ["text"] = "" }));
        Assert.Equal("Escreva o texto.", preset.ValidateParams(new JsonObject()));
        Assert.Equal("Texto longo demais (máx. 10.000 caracteres).", preset.ValidateParams(new JsonObject { ["text"] = new string('a', 10_001) }));
        Assert.Null(preset.ValidateParams(new JsonObject { ["text"] = new string('a', 10_000) }));
    }

    KeyPreset Copy => new("copy", "Copiar", "E8C8", kb, Vk.Control, 0x43);
    KeyPreset Paste => new("paste", "Colar", "E77F", kb, Vk.Control, 0x56);
    KeyPreset Mute => new("mute", "Silenciar", "E74F", kb, Vk.VolumeMute);
    KeyPreset PlayPause => new("playPause", "Tocar/Pausar", "E768", kb, Vk.MediaPlayPause);

    [Fact]
    public async Task KeyPresets_SendTheirKeys()
    {
        await Copy.RunAsync([], default);
        await Paste.RunAsync([], default);
        await Mute.RunAsync([], default);
        await PlayPause.RunAsync([], default);
        Assert.Equal([Chord(Vk.Control, 0x43), Chord(Vk.Control, 0x56), Tap(Vk.VolumeMute, 1), Tap(Vk.MediaPlayPause, 1)], kb.Calls);
        Assert.Equal(["copy", "paste", "mute", "playPause"], new[] { Copy, Paste, Mute, PlayPause }.Select(p => p.Id));
    }
}
