using System.Runtime.InteropServices;
using BioTap.Core.Actions;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace BioTap.App.Os;

/// <summary>IKeyboard through SendInput. Never logs the keys or the text it sends.</summary>
sealed class SendInputKeyboard : IKeyboard
{
    const int MaxCodeUnitsPerCall = 500; // two inputs (down, up) each

    public void Chord(IReadOnlyList<ushort> keys)
    {
        var downs = keys.Select(k => Key(k, up: false)).ToList();
        var ups = keys.Reverse().Select(k => Key(k, up: true)).ToList();
        // Releasing a key that is not down is harmless; a modifier left down is not
        try { Send(downs); }
        finally { Send(ups); }
    }

    public void Tap(ushort key, int times)
    {
        var inputs = new List<INPUT>();
        for (var i = 0; i < times; i++) { inputs.Add(Key(key, up: false)); inputs.Add(Key(key, up: true)); }
        Send(inputs);
    }

    public void Type(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { inputs.Add(Key(0x0D, up: false)); inputs.Add(Key(0x0D, up: true)); }
            else { inputs.Add(Unicode(c, up: false)); inputs.Add(Unicode(c, up: true)); }
            if (inputs.Count >= MaxCodeUnitsPerCall * 2) { Send(inputs); inputs.Clear(); }
        }
        if (inputs.Count > 0) Send(inputs);
    }

    static INPUT Key(ushort vk, bool up)
    {
        var flags = (up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY : 0);
        return new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD, Anonymous = { ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)vk, dwFlags = flags } } };
    }

    static INPUT Unicode(char c, bool up)
        => new() { type = INPUT_TYPE.INPUT_KEYBOARD, Anonymous = { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | (up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0) } } };

    // Page/arrow block, Insert, Delete and the Windows key are "extended" keys: without the flag they act as numpad keys
    static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B;

    static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var sent = PInvoke.SendInput(inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) throw new PresetException("O Windows bloqueou o envio de teclas.");
    }
}
