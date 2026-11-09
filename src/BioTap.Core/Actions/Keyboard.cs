namespace BioTap.Core.Actions;

/// <summary>Sends keys to the focused window (SendInput in the App; a fake in tests).</summary>
public interface IKeyboard
{
    /// <summary>All keys down in order, then all up in reverse order.</summary>
    void Chord(IReadOnlyList<ushort> keys);
    /// <summary>Presses and releases <paramref name="key"/> <paramref name="times"/> times.</summary>
    void Tap(ushort key, int times);
    /// <summary>Unicode text; '\n' is Enter, '\r' is ignored.</summary>
    void Type(string text);
}

/// <summary>Windows virtual-key codes.</summary>
public static class Vk
{
    public const ushort Control = 0x11, Menu = 0x12, Shift = 0x10, LWin = 0x5B,
        VolumeUp = 0xAF, VolumeDown = 0xAE, VolumeMute = 0xAD, MediaPlayPause = 0xB3;
}

/// <summary>Keys offered by the shortcut editor, named as the editor shows them.</summary>
public static class ShortcutKeys
{
    public static IReadOnlyDictionary<string, ushort> All { get; } = Build();

    static Dictionary<string, ushort> Build()
    {
        var d = new Dictionary<string, ushort>();
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (var i = 1; i <= 24; i++) d["F" + i] = (ushort)(0x70 + i - 1);
        d["Esquerda"] = 0x25; d["Cima"] = 0x26; d["Direita"] = 0x27; d["Baixo"] = 0x28;
        d["Esc"] = 0x1B; d["Tab"] = 0x09; d["Enter"] = 0x0D; d["Espaço"] = 0x20; d["Backspace"] = 0x08;
        d["Delete"] = 0x2E; d["Insert"] = 0x2D; d["Home"] = 0x24; d["End"] = 0x23;
        d["PageUp"] = 0x21; d["PageDown"] = 0x22; d["PrintScreen"] = 0x2C;
        return d;
    }
}
