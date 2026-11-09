using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

sealed class FakeKeyboard : IKeyboard
{
    public readonly List<string> Calls = [];
    public void Chord(IReadOnlyList<ushort> keys) => Calls.Add("Chord(" + string.Join(",", keys.Select(k => "0x" + k.ToString("X2"))) + ")");
    public void Tap(ushort key, int times) => Calls.Add($"Tap(0x{key:X2},{times})");
    public void Type(string text) => Calls.Add("Type:" + text);
}

sealed class FakeWindows : IWindows
{
    public nint ForegroundWindow;
    public readonly List<string> Calls = [];
    public Queue<nint>? Sequence;
    public nint Foreground() => Sequence is { Count: > 0 } ? Sequence.Dequeue() : ForegroundWindow;
    public void ToggleTopmost(nint window) => Calls.Add($"Topmost({window})");
    public void MinimizeAllExcept(nint window) => Calls.Add($"MinimizeExcept({window})");
    public void Lock() => Calls.Add("Lock");
}

sealed class FakeAudio : IAudio
{
    public List<AudioDevice> Devices = [];
    public string? Active;
    public readonly List<string> Calls = [];
    public IReadOnlyList<AudioDevice> Outputs() => Devices;
    public string? ActiveOutputId() => Active;
    public void SetDefaultOutput(string id) => Calls.Add("Default:" + id);
    public void SetVolume(int percent) => Calls.Add("Volume:" + percent);
}

sealed class FakeScreenCapture : IScreenCapture
{
    public readonly List<string> Calls = [];
    public byte[]? WindowPng = [9, 9];
    public byte[] CaptureMonitorUnderMouse() { Calls.Add("Monitor"); return [1, 2, 3]; }
    public byte[]? CaptureWindow(nint window) { Calls.Add($"Window({window})"); return WindowPng; }
    public void CopyImage(byte[] png) => Calls.Add("Copy:" + png.Length);
}

sealed class FakeClipboardText : IClipboardText
{
    public string? Text;
    public string? ReadText() => Text;
}

sealed class FakeQrWindow : IQrWindow
{
    public readonly List<string> Shown = [];
    public Exception? Throws;
    public Task ShowAsync(string text)
    {
        if (Throws is not null) throw Throws;
        Shown.Add(text);
        return Task.CompletedTask;
    }
}
