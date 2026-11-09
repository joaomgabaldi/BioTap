using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

sealed class FakeKeyboard : IKeyboard
{
    public readonly List<string> Calls = [];
    public void Chord(IReadOnlyList<ushort> keys) => Calls.Add("Chord(" + string.Join(",", keys.Select(k => "0x" + k.ToString("X2"))) + ")");
    public void Tap(ushort key, int times) => Calls.Add($"Tap(0x{key:X2},{times})");
    public void Type(string text) => Calls.Add("Type:" + text);
}
