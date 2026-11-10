using System.Text.Json.Nodes;
using BioTap.Core.Config;
using BioTap.Core.Text;

namespace BioTap.Core.Actions;

/// <summary>shortcut: { "ctrl","alt","shift","win": bool, "key": name from <see cref="ShortcutKeys"/> }.</summary>
public sealed class ShortcutPreset(IKeyboard kb) : IPreset
{
    public string Id => "shortcut";
    public string DisplayName => Strings.Current.PresetShortcut;
    public IconRef DefaultIcon => new("glyph", "E765");

    public string? ValidateParams(JsonObject p)
    {
        if (new[] { "ctrl", "alt", "shift", "win" }.Any(m => p[m] is { } n && !(n is JsonValue v && v.TryGetValue<bool>(out _)))) return Strings.Current.ShortcutInvalid;
        var name = Params.Str(p, "key");
        if (string.IsNullOrEmpty(name)) return Strings.Current.ShortcutChooseKey;
        if (!ShortcutKeys.All.ContainsKey(name)) return Strings.Current.ShortcutInvalidKey;
        bool ctrl = Params.Bool(p, "ctrl", false), alt = Params.Bool(p, "alt", false), shift = Params.Bool(p, "shift", false), win = Params.Bool(p, "win", false);
        if (win && !ctrl && !alt && !shift && name == "L") return Strings.Current.ShortcutNoWinL;
        if (ctrl && alt && name == "Delete") return Strings.Current.ShortcutNoCtrlAltDel;
        return null;
    }

    public string? CheckReady(JsonObject p) => null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        List<ushort> keys = [];
        if (Params.Bool(p, "ctrl", false)) keys.Add(Vk.Control);
        if (Params.Bool(p, "shift", false)) keys.Add(Vk.Shift);
        if (Params.Bool(p, "alt", false)) keys.Add(Vk.Menu);
        if (Params.Bool(p, "win", false)) keys.Add(Vk.LWin);
        keys.Add(ShortcutKeys.All[Params.Str(p, "key")!]);
        KeyPress.Send(kb, keys);
        return Task.CompletedTask;
    }
}

static class KeyPress
{
    /// <summary>One key is tapped; two or more are a chord.</summary>
    public static void Send(IKeyboard kb, IReadOnlyList<ushort> keys)
    {
        if (keys.Count == 1) kb.Tap(keys[0], 1); else kb.Chord(keys);
    }
}

/// <summary>A fixed key or chord without parameters (copy, paste, mute, play/pause).</summary>
public sealed class KeyPreset(string id, Func<Strings, string> name, string glyph, IKeyboard kb, params ushort[] keys) : IPreset
{
    public string Id => id;
    public string DisplayName => name(Strings.Current);
    public IconRef DefaultIcon => new("glyph", glyph);
    public bool SystemFeedback => keys is [Vk.VolumeMute];
    public string? ValidateParams(JsonObject p) => null;
    public string? CheckReady(JsonObject p) => null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        KeyPress.Send(kb, keys);
        return Task.CompletedTask;
    }
}

/// <summary>volumeUp / volumeDown: { "step": even 2–50 }. Each media-key press moves the volume 2%.</summary>
public sealed class VolumeStepPreset(IKeyboard kb, bool up) : IPreset
{
    public string Id => up ? "volumeUp" : "volumeDown";
    public bool SystemFeedback => true;
    public string DisplayName => up ? Strings.Current.PresetVolumeUp : Strings.Current.PresetVolumeDown;
    public IconRef DefaultIcon => new("glyph", up ? "E995" : "E993");

    public string? ValidateParams(JsonObject p)
        => Params.Int(p, "step") is >= 2 and <= 50 and var s && s % 2 == 0 ? null : "Escolha um passo par de 2 a 50%.";

    public string? CheckReady(JsonObject p) => null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        kb.Tap(up ? Vk.VolumeUp : Vk.VolumeDown, Params.Int(p, "step")!.Value / 2);
        return Task.CompletedTask;
    }
}

/// <summary>typeText: { "text": 1–10 000 chars }. Typed as Unicode; the text is never logged or shown.</summary>
public sealed class TypeTextPreset(IKeyboard kb) : IPreset
{
    const int MaxLength = 10_000;

    public string Id => "typeText";
    public string DisplayName => Strings.Current.PresetTypeText;
    public IconRef DefaultIcon => new("glyph", "E8D2");

    public string? ValidateParams(JsonObject p)
    {
        var text = Params.Str(p, "text");
        if (string.IsNullOrEmpty(text)) return Strings.Current.TypeWriteText;
        return text.Length > MaxLength ? Strings.Current.TypeTooLong : null;
    }

    public string? CheckReady(JsonObject p) => null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        // WinUI text boxes store line breaks as "\r": every kind becomes "\n" (Enter)
        kb.Type(Params.Str(p, "text")!.Replace("\r\n", "\n").Replace('\r', '\n'));
        return Task.CompletedTask;
    }
}
