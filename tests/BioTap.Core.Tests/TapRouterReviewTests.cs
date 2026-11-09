using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

public class TapRouterReviewTests
{
    readonly ConcurrentQueue<ActionOutcome> outcomes = new();

    static ActionConfig A(int taps, string preset = "fake", string name = "Acao")
        => new(Guid.NewGuid(), taps, name, true, new IconRef("glyph", "E70B"), preset, new JsonObject());

    TapRouter Router(IEnumerable<IPreset> presets, TimeProvider? time = null, params ActionConfig[] actions)
    {
        var cfg = AppConfig.Default with { Actions = actions };
        var r = new TapRouter(() => cfg, new PresetCatalog(presets), time);
        r.Outcome += outcomes.Enqueue;
        return r;
    }

    static async Task WaitUntil(Func<bool> c)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!c()) { if (Environment.TickCount64 > deadline) throw new TimeoutException(); await Task.Delay(5); }
    }

    [Fact]
    public async Task Failed_ShowsOnlySafeText_ForUnexpectedExceptions()
    {
        // e.g. a Win32 error whose message carries the command line or a path with personal data
        var p = new FakePreset("fake") { Throws = new InvalidOperationException(@"powershell -c secret-token C:\Users\x") };
        Router([p], null, A(1)).OnTaps(1);
        await WaitUntil(() => outcomes.Any(o => o.Kind == OutcomeKind.Failed));
        var f = outcomes.Single(o => o.Kind == OutcomeKind.Failed);
        Assert.Equal("Erro ao executar", f.Detail);
    }

    [Fact]
    public async Task Failed_ShowsPresetExceptionText()
    {
        var p = new FakePreset("fake") { Throws = new PresetException("Explorer não está rodando") };
        Router([p], null, A(1)).OnTaps(1);
        await WaitUntil(() => outcomes.Any(o => o.Kind == OutcomeKind.Failed));
        Assert.Equal("Explorer não está rodando", outcomes.Single(o => o.Kind == OutcomeKind.Failed).Detail);
    }

    [Fact]
    public async Task Failure_IsRanThenFailed_ExactlyOnce()
    {
        var p = new FakePreset("fake") { Throws = new PresetException("x") };
        Router([p], null, A(1)).OnTaps(1);
        await WaitUntil(() => outcomes.Count == 2);
        await Task.Delay(50);
        Assert.Equal([OutcomeKind.Ran, OutcomeKind.Failed], outcomes.Select(o => o.Kind));
    }

    [Fact]
    public void ConfigProviderThrows_ReportsFailed_WithoutThrowing()
    {
        var r = new TapRouter(() => throw new IOException("disk"), new PresetCatalog([]));
        r.Outcome += outcomes.Enqueue;
        r.OnTaps(1);
        Assert.Equal(OutcomeKind.Failed, Assert.Single(outcomes).Kind);
        Assert.Equal("Erro interno", outcomes.Single().Detail);
    }

    [Fact]
    public void UnknownPreset_ReportsFailed()
    {
        Router([new FakePreset("fake")], null, A(1, preset: "gone")).OnTaps(1);
        Assert.Equal(OutcomeKind.Failed, Assert.Single(outcomes).Kind);
    }

    [Fact]
    public async Task DifferentActions_RunConcurrently()
    {
        var a = new FakePreset("a") { Gate = new TaskCompletionSource() };
        var b = new FakePreset("b") { Gate = new TaskCompletionSource() };
        var r = Router([a, b], null, A(1, "a"), A(2, "b"));
        r.OnTaps(1);
        r.OnTaps(2);
        await WaitUntil(() => a.Runs == 1 && b.Runs == 1);
        a.Gate.SetResult();
        b.Gate.SetResult();
    }

    [Fact]
    public async Task HungAction_TimesOut_AndFreesTheSlot()
    {
        var time = new FakeTimeProvider();
        var p = new FakePreset("fake") { Gate = new TaskCompletionSource() }; // never completes
        var r = Router([p], time, A(1));
        r.OnTaps(1);
        await WaitUntil(() => p.Runs == 1);
        for (var i = 0; i < 70 && !outcomes.Any(o => o.Kind == OutcomeKind.Failed); i++) { time.Advance(TimeSpan.FromSeconds(1)); await Task.Delay(5); }
        Assert.Equal("Tempo esgotado", outcomes.Single(o => o.Kind == OutcomeKind.Failed).Detail);
        r.OnTaps(1);
        await WaitUntil(() => p.Runs == 2);
    }

    [Fact]
    public async Task TapRouter_UsesPresetMaxRunTime()
    {
        var time = new FakeTimeProvider();
        var p = new FakePreset("fake") { Gate = new TaskCompletionSource(), MaxRun = TimeSpan.FromSeconds(120) };
        Router([p], time, A(1)).OnTaps(1);
        await WaitUntil(() => p.Runs == 1);
        time.Advance(TimeSpan.FromSeconds(61));
        await Task.Delay(100); // timers fire synchronously on Advance; this only lets a wrong 60 s timeout surface
        Assert.DoesNotContain(outcomes, o => o.Kind == OutcomeKind.Failed);
        time.Advance(TimeSpan.FromSeconds(60));
        await WaitUntil(() => outcomes.Any(o => o.Kind == OutcomeKind.Failed));
        Assert.Equal("Tempo esgotado", outcomes.Single(o => o.Kind == OutcomeKind.Failed).Detail);
    }

    [Fact]
    public async Task Sequence_HungStep_ReportsTheStepTimeout_NotTheGenericOne()
    {
        var time = new FakeTimeProvider();
        var step = new FakePreset("hang") { HangUntilCancelled = true };
        PresetCatalog? cat = null;
        var seq = new SequencePreset(() => cat!, time);
        cat = new PresetCatalog([step, seq]);
        var args = new JsonObject { ["steps"] = new JsonArray(new JsonObject { ["preset"] = "hang", ["params"] = new JsonObject(), ["delayMs"] = 0 }) };
        var action = new ActionConfig(Guid.NewGuid(), 1, "Seq", true, new IconRef("glyph", "E70B"), "sequence", args);
        var r = new TapRouter(() => AppConfig.Default with { Actions = [action] }, cat, time);
        r.Outcome += outcomes.Enqueue;
        r.OnTaps(1);
        await WaitUntil(() => step.Runs == 1);
        for (var i = 0; i < 80 && !outcomes.Any(o => o.Kind == OutcomeKind.Failed); i++) { time.Advance(TimeSpan.FromSeconds(1)); await Task.Delay(5); }
        Assert.Equal("Passo 1 (Fake hang): Tempo esgotado", outcomes.Single(o => o.Kind == OutcomeKind.Failed).Detail);
    }

    [Fact]
    public void Catalog_DuplicateIds_NamesTheId()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new PresetCatalog([new FakePreset("x"), new FakePreset("x")]));
        Assert.Contains("x", e.Message);
    }
}

public class TapRouterTestRunTests
{
    [Fact]
    public async Task Test_OfAnActionWithInvalidParams_IsBlocked_AndNeverRuns()
    {
        // Invalid actions load disabled but stay listed with a "Testar" button: the rules apply at the run itself
        var p = new FakePreset("fake") { Invalid = "Comando com quebra de linha" };
        var outcomes = new ConcurrentQueue<ActionOutcome>();
        var action = new ActionConfig(Guid.NewGuid(), 4, "Inválida", false, new IconRef("glyph", "E70B"), "fake", new JsonObject());
        var r = new TapRouter(() => AppConfig.Default, new PresetCatalog([p]));
        r.Outcome += outcomes.Enqueue;
        r.Test(action);
        var deadline = Environment.TickCount64 + 5000;
        while (outcomes.IsEmpty && Environment.TickCount64 < deadline) await Task.Delay(5);
        await Task.Delay(50);
        Assert.Equal(0, p.Runs);
        var o = Assert.Single(outcomes);
        Assert.Equal(OutcomeKind.Blocked, o.Kind);
        Assert.Equal("Comando com quebra de linha", o.Detail);
    }

    [Fact]
    public async Task Test_RunsTheGivenAction_EvenWhenDisabled()
    {
        var p = new FakePreset("fake");
        var outcomes = new ConcurrentQueue<ActionOutcome>();
        var action = new ActionConfig(Guid.NewGuid(), 4, "Testável", false, new IconRef("glyph", "E70B"), "fake", new JsonObject());
        var r = new TapRouter(() => AppConfig.Default, new PresetCatalog([p]));
        r.Outcome += outcomes.Enqueue;
        r.Test(action);
        var deadline = Environment.TickCount64 + 5000;
        while (p.Runs == 0 && Environment.TickCount64 < deadline) await Task.Delay(5);
        Assert.Equal(1, p.Runs);
        Assert.Equal(OutcomeKind.Ran, outcomes.Single().Kind);
    }
}
