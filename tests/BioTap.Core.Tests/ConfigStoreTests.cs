using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

sealed class TestCatalog : IPresetCatalog
{
    public bool IsKnown(string preset) => preset is "openTarget" or "runCommand" or "sequence";
    public string? ValidateParams(string preset, JsonObject p) => p.ContainsKey("bad") ? "bad param" : null;
}

public sealed class ConfigStoreTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-cfg-").FullName;
    string Path_ => Path.Combine(dir, "config.json");
    static bool Secure(string _) => true;
    static readonly TestCatalog Catalog = new();

    public void Dispose() => Directory.Delete(dir, true);

    static ActionConfig Action(int taps, string preset = "openTarget", JsonObject? p = null, bool enabled = true)
        => new(Guid.NewGuid(), taps, $"a{taps}", enabled, new IconRef("glyph", "E7C3"), preset, p ?? new JsonObject { ["target"] = @"C:\x.exe" });

    [Fact]
    public void Load_Missing_ReturnsEmptyDefault()
    {
        var r = ConfigStore.Load(Path_, Secure, Catalog);
        Assert.False(r.ReadOnly);
        Assert.Empty(r.Errors);
        Assert.Equal(AppConfig.Default, r.Config);
        Assert.Equal(800, r.Config.TapTimeoutMs);
    }

    [Fact]
    public void Load_Insecure_ReadOnlyWithError()
    {
        File.WriteAllText(Path_, "{}");
        var r = ConfigStore.Load(Path_, _ => false, Catalog);
        Assert.True(r.ReadOnly);
        Assert.NotEmpty(r.Errors);
        Assert.Empty(r.Config.Actions);
    }

    [Fact]
    public void Load_InvalidJson_ReadOnlyWithError()
    {
        File.WriteAllText(Path_, "{oops");
        var r = ConfigStore.Load(Path_, Secure, Catalog);
        Assert.True(r.ReadOnly);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Load_FutureVersion_IsReadOnlyAndUntouched()
    {
        const string json = """{"version":2,"tapTimeoutMs":800,"actions":[]}""";
        File.WriteAllText(Path_, json);
        var r = ConfigStore.Load(Path_, Secure, Catalog);
        Assert.True(r.ReadOnly);
        Assert.Equal(json, File.ReadAllText(Path_));
    }

    [Fact]
    public void Load_InvalidAction_DisabledOthersKept()
    {
        var good = Action(1);
        var bad = Action(2, p: new JsonObject { ["bad"] = true });
        ConfigStore.Save(Path_, AppConfig.Default with { Actions = [good] }, Catalog);
        // inject an invalid action directly into the file
        var node = JsonNode.Parse(File.ReadAllText(Path_))!;
        node["actions"]!.AsArray().Add(JsonNode.Parse(ConfigStore.Serialize(AppConfig.Default with { Actions = [bad] }))!["actions"]![0]!.DeepClone());
        File.WriteAllText(Path_, node.ToJsonString());

        var r = ConfigStore.Load(Path_, Secure, Catalog);
        Assert.False(r.ReadOnly);
        Assert.Equal(2, r.Config.Actions.Count);
        Assert.Contains(bad.Id, r.InvalidActions);
        Assert.DoesNotContain(good.Id, r.InvalidActions);
    }

    [Fact]
    public void Save_RoundTrip()
    {
        var cfg = new AppConfig(1, 1200, new OverlayConfig(false, 3000), [Action(1), Action(3, "runCommand", new JsonObject { ["shell"] = "cmd", ["command"] = "dir" })]);
        ConfigStore.Save(Path_, cfg, Catalog);
        var r = ConfigStore.Load(Path_, Secure, Catalog);
        Assert.Equal(1200, r.Config.TapTimeoutMs);
        Assert.Equal(cfg.Overlay, r.Config.Overlay);
        Assert.Equal(cfg.Actions.Select(a => (a.Id, a.Taps, a.Name, a.Preset)), r.Config.Actions.Select(a => (a.Id, a.Taps, a.Name, a.Preset)));
        Assert.Equal("dir", r.Config.Actions[1].Params["command"]!.GetValue<string>());
    }

    [Fact]
    public void Save_IsAtomic_NoTmpLeftBehind()
    {
        ConfigStore.Save(Path_, AppConfig.Default, Catalog);
        ConfigStore.Save(Path_, AppConfig.Default with { TapTimeoutMs = 900 }, Catalog);
        Assert.Equal(["config.json"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_Invalid_ThrowsAndKeepsOldFile()
    {
        ConfigStore.Save(Path_, AppConfig.Default, Catalog);
        var before = File.ReadAllText(Path_);
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Save(Path_, AppConfig.Default with { Actions = [Action(1), Action(1)] }, Catalog));
        Assert.Equal(before, File.ReadAllText(Path_));
    }

    [Fact]
    public void Load_Oversized_ReadOnly()
    {
        File.WriteAllText(Path_, "{\"version\":1,\"pad\":\"" + new string('a', 1024 * 1024) + "\"}");
        Assert.True(ConfigStore.Load(Path_, Secure, Catalog).ReadOnly);
    }
}
