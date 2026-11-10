using System.Text.Json.Nodes;
using BioTap.Core.Text;

namespace BioTap.Core.Config;

/// <param name="ActionId">The action the error belongs to; null for settings that apply to the whole config.</param>
public sealed record ConfigError(Guid? ActionId, string Message);

public static class ConfigValidator
{
    /// <summary>
    /// Ranges, unique ids and unique tap counts apply to every action. Preset and parameter checks apply only to
    /// enabled actions, so a disabled invalid action never blocks saving the rest.
    /// </summary>
    public static IReadOnlyList<ConfigError> Validate(AppConfig c, IPresetCatalog presets)
    {
        var errors = new List<ConfigError>();
        if (c.TapTimeoutMs is < TapCounter.MinTimeoutMs or > TapCounter.MaxTimeoutMs)
            errors.Add(new(null, Strings.Current.TimeoutRange(TapCounter.MinTimeoutMs, TapCounter.MaxTimeoutMs)));
        if (c.Overlay.DurationMs is < OverlayConfig.MinDurationMs or > OverlayConfig.MaxDurationMs)
            errors.Add(new(null, Strings.Current.OverlayRange(OverlayConfig.MinDurationMs, OverlayConfig.MaxDurationMs)));

        var byTaps = new Dictionary<int, ActionConfig>();
        var ids = new HashSet<Guid>();
        foreach (var a in c.Actions)
        {
            if (a.Id == Guid.Empty || !ids.Add(a.Id))
                errors.Add(new(a.Id, Strings.Current.ActionBadId));
            if (a.Taps is < 1 or > Protocol.MaxTaps)
                errors.Add(new(a.Id, Strings.Current.TapsRange(Protocol.MaxTaps)));
            else if (!byTaps.TryAdd(a.Taps, a))
                errors.Add(new(a.Id, Strings.Current.TapsTaken(a.Taps, byTaps[a.Taps].Name)));

            if (a.Enabled && CheckPreset(a, presets) is { } bad) errors.Add(new(a.Id, bad));
        }
        return errors;
    }

    static string? CheckPreset(ActionConfig a, IPresetCatalog presets)
    {
        try
        {
            if (!presets.IsKnown(a.Preset)) return Strings.Current.UnknownPresetNamed(a.Preset);
            if (presets.ValidateParams(a.Preset, a.Params) is { } bad) return bad;
            if (a.Preset == "sequence" && HasNestedSequence(a.Params)) return Strings.Current.SeqNoNested;
            return null;
        }
        catch (Exception)
        {
            return Strings.Current.InvalidParams; // a broken preset validator only invalidates its own action
        }
    }

    static bool HasNestedSequence(JsonObject p)
        => p["steps"] is JsonArray steps
           && steps.Any(s => s is JsonObject o && o["preset"] is JsonValue v && v.TryGetValue<string>(out var name) && name == "sequence");

    /// <summary>Smallest tap count ≥ 1 not in use (fills gaps: 1, 2, 5 → 3).</summary>
    public static int NextFreeTaps(IEnumerable<int> used)
    {
        var set = used.ToHashSet();
        var n = 1;
        while (set.Contains(n)) n++;
        return n;
    }
}
