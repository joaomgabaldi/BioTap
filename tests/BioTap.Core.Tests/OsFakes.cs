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
