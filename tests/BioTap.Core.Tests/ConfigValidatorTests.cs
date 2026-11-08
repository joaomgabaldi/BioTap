using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public class ConfigValidatorTests
{
    static readonly TestCatalog Catalog = new();

    static ActionConfig Action(int taps, string preset = "openTarget", JsonObject? p = null)
        => new(Guid.NewGuid(), taps, $"a{taps}", true, new IconRef("glyph", "E7C3"), preset, p ?? new JsonObject { ["target"] = @"C:\x.exe" });

    static AppConfig With(params ActionConfig[] a) => AppConfig.Default with { Actions = a };

    [Fact]
    public void Valid_NoErrors() => Assert.Empty(ConfigValidator.Validate(With(Action(1), Action(2)), Catalog));

    [Fact]
    public void DuplicateTaps() => Assert.Contains(ConfigValidator.Validate(With(Action(2), Action(2)), Catalog), e => e.Message.Contains("2"));

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void TapsOutOfRange(int taps) => Assert.NotEmpty(ConfigValidator.Validate(With(Action(taps)), Catalog));

    [Theory]
    [InlineData(299)]
    [InlineData(3001)]
    public void TimeoutOutOfRange(int ms) => Assert.NotEmpty(ConfigValidator.Validate(AppConfig.Default with { TapTimeoutMs = ms }, Catalog));

    [Theory]
    [InlineData(499)]
    [InlineData(10001)]
    public void OverlayDurationOutOfRange(int ms)
        => Assert.NotEmpty(ConfigValidator.Validate(AppConfig.Default with { Overlay = new OverlayConfig(true, ms) }, Catalog));

    [Fact]
    public void UnknownPreset() => Assert.NotEmpty(ConfigValidator.Validate(With(Action(1, "launchRocket")), Catalog));

    [Fact]
    public void InvalidParams_ReportedForThatAction()
    {
        var bad = Action(1, p: new JsonObject { ["bad"] = true });
        var e = Assert.Single(ConfigValidator.Validate(With(bad, Action(2)), Catalog));
        Assert.Equal(bad.Id, e.ActionId);
    }

    [Fact]
    public void NestedSequence()
    {
        var seq = Action(1, "sequence", new JsonObject { ["steps"] = new JsonArray(new JsonObject { ["preset"] = "sequence" }) });
        Assert.NotEmpty(ConfigValidator.Validate(With(seq), Catalog));
    }

    [Theory]
    [InlineData(new int[0], 1)]
    [InlineData(new[] { 1, 2, 5 }, 3)]
    [InlineData(new[] { 2 }, 1)]
    [InlineData(new[] { 1, 2, 3 }, 4)]
    public void NextFreeTaps(int[] used, int expected) => Assert.Equal(expected, ConfigValidator.NextFreeTaps(used));
}
