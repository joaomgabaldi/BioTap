using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>sequence: { "steps": [ { "preset": string, "params": object, "delayMs": 0..10000 } ] }, 1 to 20 steps run in order.</summary>
public sealed class SequencePreset(Func<PresetCatalog> catalog, TimeProvider time) : IPreset
{
    public const int MaxSteps = 20;
    public const int MaxDelayMs = 10_000;
    static readonly TimeSpan PerStep = TimeSpan.FromSeconds(60);

    public string Id => "sequence";
    public string DisplayName => "Sequência de ações";
    public IconRef DefaultIcon => new("glyph", "E8FD");

    sealed record Step(string Preset, JsonObject Params, int DelayMs);

    /// <summary>Structure only (count, shape, delay, not nested); null error when fine.</summary>
    static (List<Step>? Steps, string? Error) Parse(JsonObject p)
    {
        if (p["steps"] is not JsonArray arr || arr.Count is < 1 or > MaxSteps) return (null, "Uma sequência tem de 1 a 20 passos.");
        var steps = new List<Step>();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonObject s || Params.Str(s, "preset") is not { } preset) return (null, $"Passo {i + 1}: passo inválido.");
            if (preset == "sequence") return (null, "Uma sequência não pode conter outra sequência.");
            var delay = s["delayMs"] is null ? 0 : Params.Int(s, "delayMs");
            if (delay is not (>= 0 and <= MaxDelayMs)) return (null, "Espera de 0 a 10 s.");
            JsonObject args;
            if (s["params"] is null) args = [];
            else if (s["params"] is JsonObject o) args = o;
            else return (null, $"Passo {i + 1}: parâmetros inválidos.");
            steps.Add(new Step(preset, args, delay.Value));
        }
        return (steps, null);
    }

    public string? ValidateParams(JsonObject p)
    {
        var (steps, error) = Parse(p);
        if (steps is null) return error;
        var cat = catalog();
        for (var i = 0; i < steps.Count; i++)
        {
            if (cat.Get(steps[i].Preset) is not { } preset) return $"Passo {i + 1}: preset desconhecido.";
            if (preset.ValidateParams(steps[i].Params) is { } reason) return $"Passo {i + 1} ({preset.DisplayName}): {reason}";
        }
        return null;
    }

    // Each step's own CheckReady runs right before that step (an earlier step may be what makes it ready)
    public string? CheckReady(JsonObject p) => ValidateParams(p);

    public TimeSpan MaxRunTime(JsonObject p)
    {
        var (steps, _) = Parse(p);
        return steps is null ? PerStep : steps.Aggregate(TimeSpan.Zero, (t, s) => t + TimeSpan.FromMilliseconds(s.DelayMs) + PerStep);
    }

    public async Task RunAsync(JsonObject p, CancellationToken ct)
    {
        var (steps, error) = Parse(p);
        if (steps is null) throw new PresetException(error!);
        var cat = catalog();
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var preset = cat.Get(s.Preset) ?? throw new PresetException($"Passo {i + 1}: preset desconhecido.");
            var label = $"Passo {i + 1} ({preset.DisplayName}): ";
            if (s.DelayMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(s.DelayMs), time, ct);
            try
            {
                if (preset.CheckReady(s.Params) is { } reason) throw new PresetException(label + reason);
                await preset.RunAsync(s.Params, ct).WaitAsync(preset.MaxRunTime(s.Params), time, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (PresetException e) when (e.Message.StartsWith(label, StringComparison.Ordinal)) { throw; }
            catch (TimeoutException) { throw new PresetException(label + "Tempo esgotado"); }
            catch (PresetException e) { throw new PresetException(label + e.Message); }
            catch (Exception) { throw new PresetException(label + "Erro ao executar"); }
        }
    }
}
