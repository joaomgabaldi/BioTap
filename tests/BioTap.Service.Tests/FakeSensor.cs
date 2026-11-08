using System.Collections.Concurrent;

namespace BioTap.Service.Tests;

/// <summary>Scripted sensor: Identify() blocks until the test pushes a result (or Cancel() is called).</summary>
sealed class FakeSensor : IBiometricSensor
{
    readonly BlockingCollection<IdentifyResult> results = [];
    readonly ConcurrentQueue<string> calls = new();

    public int AcquireHr { get; set; }
    public bool OpenSucceeds { get; set; } = true;
    public IdentifyResult? AlwaysReturn { get; set; }
    public int IgnoreCancels { get; set; }
    public volatile bool BlockedInIdentify;

    public IReadOnlyList<string> Calls => [.. calls];
    public int Count(string call) => calls.Count(c => c == call);

    public void Push(IdentifyResult r) => results.Add(r);

    public int AcquireFocus() { calls.Enqueue("AcquireFocus"); return AcquireHr; }
    public int ReleaseFocus() { calls.Enqueue("ReleaseFocus"); return 0; }
    public bool Open() { calls.Enqueue("Open"); return OpenSucceeds; }

    public IdentifyResult Identify()
    {
        calls.Enqueue("Identify");
        if (AlwaysReturn is { } r) return r;
        BlockedInIdentify = true;
        var result = results.Take();
        BlockedInIdentify = false;
        return result;
    }

    public void Cancel()
    {
        calls.Enqueue("Cancel");
        if (IgnoreCancels > 0) { IgnoreCancels--; return; }
        results.Add(IdentifyResult.Canceled);
    }

    public void Dispose() => calls.Enqueue("Dispose");
}
