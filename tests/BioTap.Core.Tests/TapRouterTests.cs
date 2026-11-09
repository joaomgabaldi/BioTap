using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using BioTap.Core.Actions;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

sealed class FakePreset(string id) : IPreset
{
    public string Id => id;
    public string DisplayName => "Fake " + id;
    public IconRef DefaultIcon => new("glyph", "E700");
    public string? NotReady { get; set; }
    public Exception? Throws { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public string? Invalid { get; set; }
    public int Runs;
    public Action? OnRun { get; set; }
    public bool HangUntilCancelled { get; set; }
    public CancellationToken Seen;
    public TimeSpan? MaxRun { get; set; }
    public TimeSpan MaxRunTime(JsonObject p) => MaxRun ?? TimeSpan.FromSeconds(60);
    public string? ValidateParams(JsonObject p) => Invalid;
    public string? CheckReady(JsonObject p) => NotReady;
    public async Task RunAsync(JsonObject p, CancellationToken ct)
    {
        Interlocked.Increment(ref Runs);
        OnRun?.Invoke();
        Seen = ct;
        if (HangUntilCancelled) await Task.Delay(Timeout.Infinite, ct);
        if (Gate is not null) await Gate.Task;
        if (Throws is not null) throw Throws;
    }
}

public class TapRouterTests
{
    readonly FakePreset preset = new("fake");
    readonly ConcurrentQueue<ActionOutcome> outcomes = new();

    TapRouter Router(params ActionConfig[] actions)
    {
        var cfg = AppConfig.Default with { Actions = actions };
        var r = new TapRouter(() => cfg, new PresetCatalog([preset]));
        r.Outcome += outcomes.Enqueue;
        return r;
    }

    static ActionConfig A(int taps, bool enabled = true, string name = "Abrir notas")
        => new(Guid.NewGuid(), taps, name, enabled, new IconRef("glyph", "E70B"), "fake", new JsonObject());

    static async Task WaitUntil(Func<bool> c)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!c()) { if (Environment.TickCount64 > deadline) throw new TimeoutException(); await Task.Delay(5); }
    }

    [Fact]
    public async Task Route_RunsMatchingAction()
    {
        Router(A(1), A(2, name: "Dois")).OnTaps(2);
        await WaitUntil(() => outcomes.Any(o => o.Kind == OutcomeKind.Ran));
        var o = outcomes.Single();
        Assert.Equal("Dois", o.Title);
        Assert.Equal(new IconRef("glyph", "E70B"), o.Icon);
        await WaitUntil(() => preset.Runs == 1);
    }

    [Fact]
    public void Route_NoAction_ShowsNothingConfigured()
    {
        Router(A(1)).OnTaps(7);
        var o = Assert.Single(outcomes);
        Assert.Equal(OutcomeKind.NothingConfigured, o.Kind);
        Assert.Equal("Nada configurado para 7 toques", o.Title);
    }

    [Fact]
    public void Route_DisabledAction_ShowsNothingConfigured()
    {
        Router(A(3, enabled: false)).OnTaps(3);
        Assert.Equal(OutcomeKind.NothingConfigured, Assert.Single(outcomes).Kind);
        Assert.Equal(0, preset.Runs);
    }

    [Fact]
    public async Task Route_NotReady_Blocked_DoesNotRun()
    {
        preset.NotReady = "Arquivo não encontrado";
        Router(A(1)).OnTaps(1);
        await WaitUntil(() => !outcomes.IsEmpty);
        var o = outcomes.Single();
        Assert.Equal(OutcomeKind.Blocked, o.Kind);
        Assert.Equal("Arquivo não encontrado", o.Detail);
        Assert.Equal(0, preset.Runs);
    }

    [Fact]
    public async Task Route_Throws_Failed_WithShortReason()
    {
        preset.Throws = new PresetException(new string('x', 70));
        Router(A(1)).OnTaps(1);
        await WaitUntil(() => outcomes.Any(o => o.Kind == OutcomeKind.Failed));
        var failed = outcomes.Single(o => o.Kind == OutcomeKind.Failed);
        Assert.Equal(new string('x', 70), failed.Detail);
    }

    [Fact]
    public async Task Route_SameActionWhileRunning_IsSkipped()
    {
        preset.Gate = new TaskCompletionSource();
        var r = Router(A(1));
        r.OnTaps(1);
        await WaitUntil(() => preset.Runs == 1);
        r.OnTaps(1);
        await WaitUntil(() => outcomes.Count == 2);
        Assert.Equal(OutcomeKind.Blocked, outcomes.Last().Kind);
        Assert.Equal("Ainda em execução", outcomes.Last().Detail);
        preset.Gate.SetResult();
        await Task.Delay(50);
        Assert.Equal(1, preset.Runs);
        r.OnTaps(1); // free again
        await WaitUntil(() => preset.Runs == 2);
    }

    [Fact]
    public void Route_DoesNotBlockCaller()
    {
        preset.Gate = new TaskCompletionSource();
        var sw = Stopwatch.StartNew();
        Router(A(1)).OnTaps(1);
        Assert.True(sw.ElapsedMilliseconds < 500); // generous: only proves the caller is not blocked on the 2 s+ action
        preset.Gate.SetResult();
    }

    [Fact]
    public void Route_ThrowingOutcomeHandler_DoesNotBreakRouting()
    {
        var r = Router(A(1));
        r.Outcome += _ => throw new InvalidOperationException();
        r.OnTaps(9); // must not throw
        Assert.Single(outcomes);
    }

    [Fact]
    public void Catalog_ImplementsConfigCatalog()
    {
        var c = new PresetCatalog([preset]);
        Assert.True(c.IsKnown("fake"));
        Assert.False(c.IsKnown("nope"));
        Assert.Null(c.ValidateParams("fake", new JsonObject()));
        Assert.Same(preset, c.Get("fake"));
    }
}
