using System.Text;
using System.Text.Json;

namespace BioTap.Core;

public enum SensorState { Ok, NoSensor, NoFocus, ReleasedForHello }

public abstract record Message;
public sealed record TapsMessage(int N) : Message;
public sealed record StatusMessage(SensorState State) : Message;
public sealed record SetTimeoutMessage(int Ms) : Message;

/// <summary>Wire format of the service pipe: one JSON object per line, at most <see cref="MaxLineBytes"/> bytes.</summary>
public static class Protocol
{
    public const int MaxLineBytes = 1024;

    static readonly Dictionary<SensorState, string> StateNames = new()
    {
        [SensorState.Ok] = "ok",
        [SensorState.NoSensor] = "noSensor",
        [SensorState.NoFocus] = "noFocus",
        [SensorState.ReleasedForHello] = "releasedForHello",
    };

    // Only ints and fixed state names are written, so no escaping is needed.
    public static string Serialize(Message m) => m switch
    {
        TapsMessage t => $$"""{"type":"taps","n":{{t.N}}}""",
        StatusMessage s => $$"""{"type":"status","state":"{{StateNames[s.State]}}"}""",
        SetTimeoutMessage t => $$"""{"type":"setTimeout","ms":{{t.Ms}}}""",
        _ => throw new ArgumentException($"Unknown message {m.GetType().Name}", nameof(m)),
    };

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Never throws: anything malformed, oversized or unknown returns false.</summary>
    public static bool TryParse(string line, out Message? m)
    {
        m = null;
        if (string.IsNullOrEmpty(line) || line.Length > MaxLineBytes) return false;
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(line); }
        catch (EncoderFallbackException) { return false; } // lone surrogates
        if (bytes.Length > MaxLineBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out var type)) return false;
            m = type switch
            {
                "taps" when TryGetInt(root, "n", out var n) && n > 0 => new TapsMessage(n),
                "status" when TryGetString(root, "state", out var s) && TryState(s, out var st) => new StatusMessage(st),
                "setTimeout" when TryGetInt(root, "ms", out var ms) && ms is >= TapCounter.MinTimeoutMs and <= TapCounter.MaxTimeoutMs => new SetTimeoutMessage(ms),
                _ => null,
            };
            return m is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static bool TryGetString(JsonElement o, string name, out string value)
    {
        value = "";
        if (!o.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString()!;
        return true;
    }

    static bool TryGetInt(JsonElement o, string name, out int value)
    {
        value = 0;
        return o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value);
    }

    static bool TryState(string name, out SensorState state)
    {
        foreach (var (k, v) in StateNames)
            if (v == name) { state = k; return true; }
        state = default;
        return false;
    }
}
