using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

public enum OutcomeKind { Ran, Blocked, NothingConfigured, Failed }

/// <summary>What the overlay shows after a tap sequence (spec §4).</summary>
public sealed record ActionOutcome(OutcomeKind Kind, string Title, string? Detail, IconRef? Icon);

public interface IPreset
{
    string Id { get; }
    string DisplayName { get; }
    IconRef DefaultIcon { get; }
    /// <summary>Static check when saving the config; null = ok.</summary>
    string? ValidateParams(JsonObject p);
    /// <summary>Check right before running (file exists, clipboard has text…); null = ok.</summary>
    string? CheckReady(JsonObject p);
    Task RunAsync(JsonObject p, CancellationToken ct);
    /// <summary>Time the router lets this run before giving up.</summary>
    TimeSpan MaxRunTime(JsonObject p) => TimeSpan.FromSeconds(60);
    /// <summary>Windows already shows the result (volume/mute flyout): no confirmation on success, errors still show.</summary>
    bool SystemFeedback => false;
}

public sealed class PresetCatalog(IEnumerable<IPreset> presets) : IPresetCatalog
{
    readonly Dictionary<string, IPreset> byId = Index(presets);

    static Dictionary<string, IPreset> Index(IEnumerable<IPreset> presets)
    {
        var d = new Dictionary<string, IPreset>();
        foreach (var p in presets)
            if (!d.TryAdd(p.Id, p)) throw new InvalidOperationException($"Preset duplicado: {p.Id}");
        return d;
    }

    public IReadOnlyCollection<IPreset> All => byId.Values;
    public IPreset? Get(string id) => byId.GetValueOrDefault(id);
    public bool IsKnown(string preset) => byId.ContainsKey(preset);
    public string? ValidateParams(string preset, JsonObject p) => byId.TryGetValue(preset, out var x) ? x.ValidateParams(p) : "Preset desconhecido.";
}
