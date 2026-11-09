using System.Text.Json.Nodes;
using BioTap.Core.Actions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BioTap.Core.Tests;

public class SequencePresetTests
{
    readonly FakeTimeProvider time = new();
    readonly FakePreset a = new("a"), b = new("b"), c = new("c");
    readonly SequencePreset seq;

    public SequencePresetTests()
    {
        PresetCatalog? cat = null;
        seq = new SequencePreset(() => cat!, time);
        cat = new PresetCatalog([a, b, c, seq]);
    }

    static JsonObject Steps(params (string Preset, int Delay)[] s)
        => new() { ["steps"] = new JsonArray([.. s.Select(x => (JsonNode?)new JsonObject { ["preset"] = x.Preset, ["params"] = new JsonObject(), ["delayMs"] = x.Delay })]) };

    static async Task Until(Func<bool> cond)
    {
        var end = Environment.TickCount64 + 5000;
        while (!cond()) { if (Environment.TickCount64 > end) throw new TimeoutException(); await Task.Delay(2); }
    }

    [Fact]
    public async Task RunsStepsInOrder_WaitingEachDelay()
    {
        var log = new List<string>();
        a.OnRun = () => log.Add("a");
        b.OnRun = () => log.Add("b");
        var run = seq.RunAsync(Steps(("a", 0), ("b", 2000)), CancellationToken.None);
        await Until(() => a.Runs == 1);
        time.Advance(TimeSpan.FromMilliseconds(1999));
        await Task.Delay(30);
        Assert.Equal(0, b.Runs);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await run;
        Assert.Equal(["a", "b"], log);
    }

    [Fact]
    public async Task StopsAtFirstFailure_NamingTheStep()
    {
        b.Throws = new PresetException("x");
        var e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(Steps(("a", 0), ("b", 0), ("c", 0)), CancellationToken.None));
        Assert.Equal("Passo 2 (Fake b): x", e.Message);
        Assert.Equal(0, c.Runs);

        b.Throws = new InvalidOperationException("segredo C:\\x");
        e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(Steps(("a", 0), ("b", 0), ("c", 0)), CancellationToken.None));
        Assert.Equal("Passo 2 (Fake b): Erro ao executar", e.Message);
    }

    [Fact]
    public async Task StepThrowingOperationCanceledOnItsOwn_IsAnExecutionError()
    {
        b.Throws = new OperationCanceledException();
        var e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(Steps(("b", 0)), CancellationToken.None));
        Assert.Equal("Passo 1 (Fake b): Erro ao executar", e.Message);
    }

    [Fact]
    public async Task StepTimeout_NamesTheStep()
    {
        b.Gate = new TaskCompletionSource();
        b.MaxRun = TimeSpan.FromSeconds(5);
        var run = seq.RunAsync(Steps(("b", 0)), CancellationToken.None);
        await Until(() => b.Runs == 1);
        time.Advance(TimeSpan.FromSeconds(6));
        var e = await Assert.ThrowsAsync<PresetException>(() => run);
        Assert.Equal("Passo 1 (Fake b): Tempo esgotado", e.Message);
    }

    [Fact]
    public async Task InvalidStep_BlocksBeforeAnyStepRuns()
    {
        c.Invalid = "motivo";
        var p = Steps(("a", 0), ("b", 0), ("c", 0));
        Assert.Equal("Passo 3 (Fake c): motivo", seq.CheckReady(p));
        Assert.Equal("Passo 3 (Fake c): motivo", seq.ValidateParams(p));
        var e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(p, CancellationToken.None));
        Assert.Equal("Passo 3 (Fake c): motivo", e.Message);
        Assert.Equal(0, a.Runs + b.Runs + c.Runs);
        e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(Steps(("a", 0), ("sequence", 0)), CancellationToken.None));
        Assert.Equal("Uma sequência não pode conter outra sequência.", e.Message);
        Assert.Equal(0, a.Runs);
    }

    [Fact]
    public async Task StepTimeout_CancelsTheStepToken()
    {
        b.HangUntilCancelled = true;
        b.MaxRun = TimeSpan.FromSeconds(5);
        var run = seq.RunAsync(Steps(("b", 0)), CancellationToken.None);
        await Until(() => b.Runs == 1);
        time.Advance(TimeSpan.FromSeconds(6));
        var e = await Assert.ThrowsAsync<PresetException>(() => run);
        Assert.Equal("Passo 1 (Fake b): Tempo esgotado", e.Message);
        Assert.True(b.Seen.IsCancellationRequested);
    }

    [Fact]
    public async Task OuterCancel_DuringDelay_IsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var run = seq.RunAsync(Steps(("a", 0), ("b", 5000)), cts.Token);
        await Until(() => a.Runs == 1);
        await Task.Delay(30);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, b.Runs);
    }

    [Fact]
    public async Task StepNotReady_StopsThere()
    {
        b.NotReady = "Arquivo não encontrado";
        var e = await Assert.ThrowsAsync<PresetException>(() => seq.RunAsync(Steps(("a", 0), ("b", 0), ("c", 0)), CancellationToken.None));
        Assert.Equal("Passo 2 (Fake b): Arquivo não encontrado", e.Message);
        Assert.Equal(1, a.Runs);
        Assert.Equal(0, b.Runs + c.Runs);
        Assert.Null(seq.CheckReady(Steps(("a", 0), ("b", 0)))); // static check does not look at step readiness
    }

    [Fact]
    public void Validate()
    {
        Assert.Null(seq.ValidateParams(Steps(("a", 0))));
        Assert.Equal("Uma sequência tem de 1 a 20 passos.", seq.ValidateParams(Steps()));
        Assert.Equal("Uma sequência tem de 1 a 20 passos.", seq.ValidateParams([]));
        Assert.Equal("Uma sequência tem de 1 a 20 passos.", seq.ValidateParams(Steps(Enumerable.Repeat(("a", 0), 21).ToArray())));
        Assert.Null(seq.ValidateParams(Steps(Enumerable.Repeat(("a", 0), 20).ToArray())));
        Assert.Equal("Uma sequência não pode conter outra sequência.", seq.ValidateParams(Steps(("a", 0), ("sequence", 0))));
        Assert.Equal("Espera de 0 a 10 s.", seq.ValidateParams(Steps(("a", -1))));
        Assert.Equal("Espera de 0 a 10 s.", seq.ValidateParams(Steps(("a", 10_001))));
        Assert.Null(seq.ValidateParams(Steps(("a", 10_000))));
        Assert.Equal("Passo 2: preset desconhecido.", seq.ValidateParams(Steps(("a", 0), ("nope", 0))));
    }

    [Fact]
    public void Validate_MissingOrBadParams()
    {
        var p = Steps(("a", 0));
        ((JsonObject)p["steps"]![0]!).Remove("params");
        Assert.Null(seq.ValidateParams(p));
        ((JsonObject)p["steps"]![0]!)["params"] = "x";
        Assert.Equal("Passo 1: parâmetros inválidos.", seq.ValidateParams(p));
    }

    [Fact]
    public void MaxRunTime_CoversDelaysAndSteps()
        => Assert.Equal(TimeSpan.FromSeconds(135), seq.MaxRunTime(Steps(("a", 0), ("b", 5000))));

    [Fact]
    public void MaxRunTime_UsesEachStepsOwn()
    {
        b.MaxRun = TimeSpan.FromSeconds(100);
        Assert.Equal(TimeSpan.FromSeconds(60 + 5 + 100 + 60 + 15), seq.MaxRunTime(Steps(("a", 0), ("b", 5000), ("nope", 0))));
    }
}
