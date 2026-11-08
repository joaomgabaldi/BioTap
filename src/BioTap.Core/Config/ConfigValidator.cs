using System.Text.Json.Nodes;

namespace BioTap.Core.Config;

/// <param name="ActionId">The action the error belongs to; null for settings that apply to the whole config.</param>
public sealed record ConfigError(Guid? ActionId, string Message);

public static class ConfigValidator
{
    public static IReadOnlyList<ConfigError> Validate(AppConfig c, IPresetCatalog presets)
    {
        var errors = new List<ConfigError>();
        if (c.TapTimeoutMs is < TapCounter.MinTimeoutMs or > TapCounter.MaxTimeoutMs)
            errors.Add(new(null, $"Tempo da sequência precisa estar entre {TapCounter.MinTimeoutMs} e {TapCounter.MaxTimeoutMs} ms."));
        if (c.Overlay.DurationMs is < OverlayConfig.MinDurationMs or > OverlayConfig.MaxDurationMs)
            errors.Add(new(null, $"Duração do overlay precisa estar entre {OverlayConfig.MinDurationMs} e {OverlayConfig.MaxDurationMs} ms."));

        var byTaps = new Dictionary<int, ActionConfig>();
        foreach (var a in c.Actions)
        {
            if (a.Taps is < 1 or > Protocol.MaxTaps)
                errors.Add(new(a.Id, $"Toques precisam estar entre 1 e {Protocol.MaxTaps}."));
            else if (!byTaps.TryAdd(a.Taps, a))
                errors.Add(new(a.Id, $"{a.Taps} toques já usado por {byTaps[a.Taps].Name}."));

            if (!presets.IsKnown(a.Preset))
                errors.Add(new(a.Id, $"Preset desconhecido: {a.Preset}."));
            else if (presets.ValidateParams(a.Preset, a.Params) is { } bad)
                errors.Add(new(a.Id, bad));
            else if (a.Preset == "sequence" && HasNestedSequence(a.Params))
                errors.Add(new(a.Id, "Uma sequência não pode conter outra sequência."));
        }
        return errors;
    }

    static bool HasNestedSequence(JsonObject p)
        => p["steps"] is JsonArray steps && steps.Any(s => s is JsonObject o && o["preset"]?.GetValue<string>() == "sequence");

    /// <summary>Smallest tap count ≥ 1 not in use (fills gaps: 1, 2, 5 → 3).</summary>
    public static int NextFreeTaps(IEnumerable<int> used)
    {
        var set = used.ToHashSet();
        var n = 1;
        while (set.Contains(n)) n++;
        return n;
    }
}
