using System.Text.Json.Nodes;
using BioTap.Core.Actions;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public sealed class ActionEditorTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-ed-").FullName;
    string P => Path.Combine(dir, "config.json");
    static readonly PresetCatalog Catalog = new([new FakePreset("fake")]);
    public void Dispose() => Directory.Delete(dir, true);

    static ActionConfig A(int taps, string name)
        => new(Guid.NewGuid(), taps, name, true, new IconRef("glyph", "E70B"), "fake", new JsonObject());

    [Fact]
    public void NewAction_PrefillsNextFreeTaps_AndPresetDefaults()
    {
        var cfg = AppConfig.Default with { Actions = [A(1, "um"), A(2, "dois"), A(5, "cinco")] };
        var a = ActionEditor.NewAction(cfg, Catalog.Get("fake")!);
        Assert.Equal(3, a.Taps);
        Assert.Equal("Fake fake", a.Name);
        Assert.Equal(new IconRef("glyph", "E700"), a.Icon);
        Assert.True(a.Enabled);
        Assert.NotEqual(Guid.Empty, a.Id);
    }

    [Fact]
    public void DuplicateTaps_ShowsUsedBy()
    {
        var existing = A(2, "Trocar áudio");
        var cfg = AppConfig.Default with { Actions = [existing] };
        Assert.Equal("Já usado por Trocar áudio", ActionEditor.Check(cfg, A(2, "nova"), Catalog));
    }

    [Fact]
    public void EditingSameActionKeepingItsTaps_IsFine()
    {
        var existing = A(2, "x");
        var cfg = AppConfig.Default with { Actions = [existing] };
        Assert.Null(ActionEditor.Check(cfg, existing with { Name = "y" }, Catalog));
    }

    [Fact]
    public void WithAction_ReplacesById_OrAppends()
    {
        var a = A(1, "a");
        var cfg = AppConfig.Default with { Actions = [a] };
        Assert.Equal("b", ActionEditor.WithAction(cfg, a with { Name = "b" }).Actions.Single().Name);
        Assert.Equal(2, ActionEditor.WithAction(cfg, A(2, "c")).Actions.Count);
        Assert.Empty(ActionEditor.Without(cfg, a.Id).Actions);
    }

    [Fact]
    public async Task Save_WritesConfigAndSendsTimeoutWhenChanged()
    {
        var sent = new List<int>();
        var saver = new SettingsSaver(P, Catalog, _ => true, (ms, _) => { sent.Add(ms); return Task.CompletedTask; });
        Assert.Equal(new SaveOutcome(true, null), await saver.SaveAsync(AppConfig.Default, AppConfig.Default with { TapTimeoutMs = 1200 }));
        Assert.Equal([1200], sent);
        Assert.Equal(new SaveOutcome(true, null), await saver.SaveAsync(AppConfig.Default with { TapTimeoutMs = 1200 }, AppConfig.Default with { TapTimeoutMs = 1200, Overlay = new(false, 1500) }));
        Assert.Equal([1200], sent); // unchanged timeout: nothing sent
        Assert.Equal(1200, ConfigStore.Load(P, _ => true, Catalog).Config.TapTimeoutMs);
    }

    [Fact]
    public async Task ReadOnlyConfig_SaveRefusedWithReason()
    {
        File.WriteAllText(P, """{"version":9}""");
        var saver = new SettingsSaver(P, Catalog, _ => true, (_, _) => Task.CompletedTask);
        var outcome = await saver.SaveAsync(AppConfig.Default, AppConfig.Default with { TapTimeoutMs = 900 });
        Assert.False(outcome.Saved);
        Assert.NotNull(outcome.Message);
        Assert.Equal("""{"version":9}""", File.ReadAllText(P));
    }
}
