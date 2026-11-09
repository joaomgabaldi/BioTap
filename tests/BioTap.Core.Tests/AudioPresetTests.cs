using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

public sealed class AudioPresetTests
{
    readonly FakeAudio audio = new();

    static HashSet<string> Set(params string[] ids) => [.. ids];
    static JsonObject Ids(params object?[] ids) => new() { ["deviceIds"] = new JsonArray([.. ids.Select(i => i is null ? null : JsonValue.Create(i))]) };

    [Fact]
    public void Next_GoesToTheOneAfterTheActive()
    {
        string[] cycle = ["a", "b", "c"];
        Assert.Equal("c", AudioCyclePreset.Next(cycle, Set("a", "b", "c"), "b"));
        Assert.Equal("a", AudioCyclePreset.Next(cycle, Set("a", "b", "c"), "c"));
    }

    [Fact]
    public void Next_SkipsAbsent() => Assert.Equal("c", AudioCyclePreset.Next(["a", "b", "c"], Set("a", "c"), "a"));

    [Fact]
    public void Next_ActiveNotInCycle_GoesToFirstPresent() => Assert.Equal("b", AudioCyclePreset.Next(["a", "b"], Set("b"), "x"));

    [Fact]
    public void Next_NonePresent_IsNull() => Assert.Null(AudioCyclePreset.Next(["a", "b"], Set(), "a"));

    [Fact]
    public void Next_OnlyActivePresent_IsNull()
    {
        Assert.Null(AudioCyclePreset.Next(["a", "b"], Set("a"), "a"));
        audio.Devices = [new("a", "A")];
        audio.Active = "a";
        Assert.Equal("Nenhum outro dispositivo da lista está conectado", new AudioCyclePreset(audio).CheckReady(Ids("a", "b")));
    }

    [Fact]
    public void AudioCycle_Validate()
    {
        var p = new AudioCyclePreset(audio);
        Assert.Equal("Escolha pelo menos 2 dispositivos.", p.ValidateParams(Ids("a")));
        Assert.Equal("Escolha pelo menos 2 dispositivos.", p.ValidateParams([]));
        Assert.Equal("No máximo 10 dispositivos.", p.ValidateParams(Ids(Enumerable.Range(0, 11).Select(i => (object)("d" + i)).ToArray())));
        Assert.Equal("Dispositivo repetido.", p.ValidateParams(Ids("a", "a")));
        Assert.Equal("Dispositivo inválido.", p.ValidateParams(Ids("a", 5)));
        Assert.Equal("Dispositivo inválido.", p.ValidateParams(Ids("a", "")));
        Assert.Equal("Dispositivo inválido.", p.ValidateParams(Ids("a", null)));
        Assert.Null(p.ValidateParams(Ids("a", "b")));
    }

    [Fact]
    public void AudioCycle_NonePresent_IsNotReady()
    {
        audio.Devices = [new("x", "X")];
        Assert.Equal("Nenhum dispositivo da lista está conectado", new AudioCyclePreset(audio).CheckReady(Ids("a", "b")));
    }

    [Fact]
    public async Task AudioCycle_Run_SetsTheNext()
    {
        audio.Devices = [new("a", "A"), new("b", "B"), new("c", "C")];
        audio.Active = "b";
        var p = new AudioCyclePreset(audio);
        Assert.Null(p.CheckReady(Ids("a", "b", "c")));
        await p.RunAsync(Ids("a", "b", "c"), default);
        Assert.Equal(["Default:c"], audio.Calls);
    }

    [Fact]
    public void VolumeSet_Validate()
    {
        var p = new VolumeSetPreset(audio);
        foreach (var bad in new JsonObject[] { new() { ["percent"] = -1 }, new() { ["percent"] = 101 }, [], new() { ["percent"] = "50" } })
            Assert.Equal("Escolha de 0 a 100%.", p.ValidateParams(bad));
        Assert.Null(p.ValidateParams(new() { ["percent"] = 0 }));
        Assert.Null(p.ValidateParams(new() { ["percent"] = 100 }));
    }

    [Fact]
    public void VolumeSet_NoActiveOutput_IsNotReady() => Assert.Equal("Nenhuma saída de áudio ativa", new VolumeSetPreset(audio).CheckReady(new() { ["percent"] = 10 }));

    [Fact]
    public async Task VolumeSet_Run_SetsPercent()
    {
        audio.Active = "a";
        var p = new VolumeSetPreset(audio);
        Assert.Null(p.CheckReady(new() { ["percent"] = 35 }));
        await p.RunAsync(new() { ["percent"] = 35 }, default);
        Assert.Equal(["Volume:35"], audio.Calls);
    }
}
