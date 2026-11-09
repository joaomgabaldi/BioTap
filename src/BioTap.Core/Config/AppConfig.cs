using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BioTap.Core.Config;

/// <param name="Kind">"glyph" (Segoe Fluent code point), "exe" or "preset".</param>
public sealed record IconRef(string Kind, string Value)
{
    /// <summary>The glyph as text, or "" when it is not a printable Unicode code point (config is hand-editable).</summary>
    public string GlyphText()
        => Kind == "glyph" && int.TryParse(Value, System.Globalization.NumberStyles.HexNumber, null, out var cp)
           && cp is >= 0x20 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
            ? char.ConvertFromUtf32(cp) : "";
}

public sealed record ActionConfig(Guid Id, int Taps, string Name, bool Enabled, IconRef Icon, string Preset, JsonObject Params)
{
    /// <summary>Fields this version does not know; kept so saving never drops them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record OverlayConfig(bool Enabled, int DurationMs)
{
    public const int MinDurationMs = 500;
    public const int MaxDurationMs = 10_000;
    public static OverlayConfig Default { get; } = new(true, 1500);
}

/// <summary>config.json (spec §5). The service reads only <see cref="TapTimeoutMs"/>.</summary>
public sealed record AppConfig(int Version, int TapTimeoutMs, OverlayConfig Overlay, IReadOnlyList<ActionConfig> Actions)
{
    public const int CurrentVersion = 1;
    public static AppConfig Default { get; } = new(CurrentVersion, ServiceConfig.Default.TapTimeoutMs, OverlayConfig.Default, []);

    /// <summary>Fields this version does not know; kept so saving never drops them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public interface IPresetCatalog
{
    bool IsKnown(string preset);
    /// <summary>null = valid.</summary>
    string? ValidateParams(string preset, JsonObject p);
}
