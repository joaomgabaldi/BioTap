using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

sealed class ThrowingCatalog : IPresetCatalog
{
    public bool IsKnown(string preset) => true;
    public string? ValidateParams(string preset, JsonObject p) => preset == "boom" ? throw new InvalidOperationException() : null;
}

public sealed class ConfigStoreReviewTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-cfg2-").FullName;
    string P => Path.Combine(dir, "config.json");
    static readonly TestCatalog Catalog = new();
    static bool Secure(string _) => true;
    public void Dispose() => Directory.Delete(dir, true);

    static ActionConfig A(int taps, string preset = "openTarget", JsonObject? p = null, bool enabled = true, Guid? id = null)
        => new(id ?? Guid.NewGuid(), taps, $"acao{taps}", enabled, new IconRef("glyph", "E7C3"), preset, p ?? new JsonObject { ["target"] = @"C:\x.exe" });

    [Fact]
    public void Save_OverUnreadableFile_RefusesAndLeavesItUntouched()
    {
        const string future = """{"version":2,"actions":[]}""";
        File.WriteAllText(P, future);
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Save(P, AppConfig.Default, Catalog, Secure));
        Assert.Equal(future, File.ReadAllText(P));
    }

    [Fact]
    public void Save_OverInsecureFile_Refuses()
    {
        ConfigStore.Save(P, AppConfig.Default, Catalog, Secure);
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Save(P, AppConfig.Default, Catalog, _ => false));
    }

    [Fact]
    public void Load_InvalidAction_ComesBackDisabled_AndConfigStillSaves()
    {
        var bad = A(2, p: new JsonObject { ["bad"] = true });
        File.WriteAllText(P, ConfigStore.Serialize(AppConfig.Default with { Actions = [A(1), bad] }));
        var r = ConfigStore.Load(P, Secure, Catalog);
        Assert.False(r.Config.Actions.Single(a => a.Id == bad.Id).Enabled);
        ConfigStore.Save(P, r.Config, Catalog, Secure); // must not throw because of the disabled invalid action
    }

    [Fact]
    public void Validate_DisabledActionWithBadParams_IsAllowed()
        => Assert.Empty(ConfigValidator.Validate(AppConfig.Default with { Actions = [A(1, p: new JsonObject { ["bad"] = true }, enabled: false)] }, Catalog));

    [Fact]
    public void Validate_NonStringPresetInSteps_DoesNotThrow()
        => ConfigValidator.Validate(AppConfig.Default with { Actions = [A(1, "sequence", new JsonObject { ["steps"] = new JsonArray(new JsonObject { ["preset"] = 5 }) })] }, Catalog);

    [Fact]
    public void Validate_ThrowingCatalog_OnlyThatActionInvalid()
    {
        var boom = A(1, "boom");
        var e = Assert.Single(ConfigValidator.Validate(AppConfig.Default with { Actions = [boom, A(2)] }, new ThrowingCatalog()));
        Assert.Equal(boom.Id, e.ActionId);
    }

    [Fact]
    public void Validate_DuplicateOrEmptyIds()
    {
        var id = Guid.NewGuid();
        Assert.NotEmpty(ConfigValidator.Validate(AppConfig.Default with { Actions = [A(1, id: id), A(2, id: id)] }, Catalog));
        Assert.NotEmpty(ConfigValidator.Validate(AppConfig.Default with { Actions = [A(1, id: Guid.Empty)] }, Catalog));
    }

    [Fact]
    public void Validate_DuplicateTaps_MessageNamesTheOtherAction()
    {
        var first = A(2);
        var second = A(2) with { Name = "segunda" };
        var e = Assert.Single(ConfigValidator.Validate(AppConfig.Default with { Actions = [first, second] }, Catalog));
        Assert.Equal(second.Id, e.ActionId);
        Assert.Equal("2 toques já usado por acao2.", e.Message);
    }

    [Fact]
    public void Save_WithStaleTmpFile_Succeeds_AndCleansUp()
    {
        File.WriteAllText(P + ".tmp", "garbage");
        ConfigStore.Save(P, AppConfig.Default, Catalog, Secure);
        Assert.False(File.Exists(P + ".tmp"));
    }

    [Fact]
    public async Task Save_WhileAReaderHoldsTheFile_RetriesAndSucceeds()
    {
        ConfigStore.Save(P, AppConfig.Default, Catalog, Secure);
        var reader = new FileStream(P, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(150); await reader.DisposeAsync(); });
        ConfigStore.Save(P, AppConfig.Default with { TapTimeoutMs = 900 }, Catalog, Secure);
        await release;
        Assert.Equal(900, ConfigStore.Load(P, Secure, Catalog).Config.TapTimeoutMs);
    }

    [Fact]
    public void Save_KeepsTheExistingFileAcl()
    {
        ConfigStore.Save(P, AppConfig.Default, Catalog, Secure);
        var fi = new FileInfo(P);
        var acl = fi.GetAccessControl();
        var marker = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null);
        acl.AddAccessRule(new FileSystemAccessRule(marker, FileSystemRights.Read, AccessControlType.Allow));
        fi.SetAccessControl(acl);
        ConfigStore.Save(P, AppConfig.Default with { TapTimeoutMs = 900 }, Catalog, Secure);
        var rules = new FileInfo(P).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        Assert.Contains(rules, r => r.IdentityReference.Equals(marker));
    }

    [Fact]
    public void UnknownFields_SurviveSave()
    {
        File.WriteAllText(P, """{"version":1,"tapTimeoutMs":800,"overlay":{"enabled":true,"durationMs":1500},"actions":[],"futureSetting":{"x":1}}""");
        var r = ConfigStore.Load(P, Secure, Catalog);
        ConfigStore.Save(P, r.Config with { TapTimeoutMs = 900 }, Catalog, Secure);
        Assert.Contains("futureSetting", File.ReadAllText(P));
    }

    [Theory]
    [InlineData("""{"version":1,"tapTimeoutMs":800,"overlay":{"enabled":true,"durationMs":1500},"actions":[{"id":"6d2f0d43-6d55-4a8c-9b1d-0a0a0a0a0a0a","taps":1,"name":null,"enabled":true,"icon":{"kind":"glyph","value":"E7C3"},"preset":"openTarget","params":{}}]}""")]
    [InlineData("""{"version":1,"tapTimeoutMs":800,"overlay":{"enabled":true,"durationMs":1500},"actions":[{"id":"6d2f0d43-6d55-4a8c-9b1d-0a0a0a0a0a0a","taps":1,"name":"x","enabled":true,"icon":{"kind":null,"value":"E7C3"},"preset":"openTarget","params":{}}]}""")]
    [InlineData("""{"tapTimeoutMs":800}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"version":"1"}""")]
    [InlineData("""{"version":1,"tapTimeoutMs":"800","overlay":{"enabled":true,"durationMs":1500},"actions":[]}""")]
    public void Load_MalformedShapes_AreReadOnly(string json)
    {
        File.WriteAllText(P, json);
        Assert.True(ConfigStore.Load(P, Secure, Catalog).ReadOnly);
    }

    [Fact]
    public void Load_TooDeep_IsReadOnly()
    {
        File.WriteAllText(P, "{\"version\":1,\"x\":" + new string('[', 40) + new string(']', 40) + "}");
        Assert.True(ConfigStore.Load(P, Secure, Catalog).ReadOnly);
    }
}
