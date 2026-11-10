using System.Text.Json.Nodes;
using BioTap.Core.Config;
using BioTap.Core.Text;

namespace BioTap.Core.Actions;

public sealed record AudioDevice(string Id, string Name);

public interface IAudio
{
    /// <summary>Active (present) render endpoints only.</summary>
    IReadOnlyList<AudioDevice> Outputs();
    string? ActiveOutputId();
    void SetDefaultOutput(string id);
    void SetVolume(int percent);
}

/// <summary>audioCycle: { "deviceIds": string[2–10 unique] }. Switches the default output to the next connected one in the list.</summary>
public sealed class AudioCyclePreset(IAudio audio) : IPreset
{
    public string Id => "audioCycle";
    public string DisplayName => Strings.Current.PresetAudioCycle;
    public IconRef DefaultIcon => new("glyph", "E7F5");

    public string? ValidateParams(JsonObject p)
    {
        if (p["deviceIds"] is not JsonArray a || a.Count < 2) return Strings.Current.AudioMinDevices;
        if (a.Count > 10) return Strings.Current.AudioMaxDevices;
        HashSet<string> seen = [];
        foreach (var n in a)
        {
            if (n is not JsonValue v || !v.TryGetValue<string>(out var id) || id.Length == 0) return Strings.Current.AudioInvalidDevice;
            if (!seen.Add(id)) return Strings.Current.AudioRepeatedDevice;
        }
        return null;
    }

    public string? CheckReady(JsonObject p)
    {
        var cycle = Ids(p);
        var present = audio.Outputs().Select(d => d.Id).ToHashSet();
        if (!cycle.Any(present.Contains)) return Strings.Current.AudioNoneConnected;
        return Next(cycle, present, audio.ActiveOutputId()) is null ? Strings.Current.AudioNoOtherConnected : null;
    }

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        var present = audio.Outputs().Select(d => d.Id).ToHashSet();
        if (Next(Ids(p), present, audio.ActiveOutputId()) is { } next) audio.SetDefaultOutput(next);
        return Task.CompletedTask;
    }

    static List<string> Ids(JsonObject p)
        => p["deviceIds"] is JsonArray a ? [.. a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s.Length > 0)] : [];

    /// <summary>The next present device after the active one in the cycle (wrapping); null when there is nothing else to switch to.</summary>
    public static string? Next(IReadOnlyList<string> cycle, IReadOnlySet<string> present, string? active)
    {
        int n = cycle.Count, at = active is null ? -1 : cycle.ToList().IndexOf(active);
        for (int i = 1; i <= n; i++)
        {
            var c = cycle[(at + i) % n];
            if (c != active && present.Contains(c)) return c;
        }
        return null;
    }
}

/// <summary>volumeSet: { "percent": 0–100 }.</summary>
public sealed class VolumeSetPreset(IAudio audio) : IPreset
{
    public string Id => "volumeSet";
    public string DisplayName => Strings.Current.PresetVolumeSet;
    public IconRef DefaultIcon => new("glyph", "E767");

    public string? ValidateParams(JsonObject p) => Params.Int(p, "percent") is >= 0 and <= 100 ? null : Strings.Current.VolumeRange;

    public string? CheckReady(JsonObject p) => audio.ActiveOutputId() is null ? Strings.Current.AudioNoActiveOutput : null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        audio.SetVolume(Params.Int(p, "percent")!.Value);
        return Task.CompletedTask;
    }
}

public static class AudioIds
{
    const string Marker = "MMDEVAPI#";

    /// <summary>MMDevice id out of a WinRT device id (<c>\?\SWD#MMDEVAPI#{id}#{interface}</c>); null when it does not match.</summary>
    public static string? FromWinRt(string winrtId)
    {
        var start = winrtId.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += Marker.Length;
        var end = winrtId.IndexOf('#', start);
        return end > start ? winrtId[start..end] : null;
    }
}
