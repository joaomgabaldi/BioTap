using System.Collections.Concurrent;

namespace BioTap.Service.Tests;

/// <summary>Scripted sensor: Identify() blocks until the test pushes a result (or Cancel() is called).</summary>
sealed class FakeSensor : IBiometricSensor
{
    readonly BlockingCollection<IdentifyResult> results = [];
    readonly ConcurrentQueue<string> calls = new();
    int identifyCalls;

    public int AcquireHr { get; set; }
    public int ReleaseHr { get; set; }
    public bool OpenSucceeds { get; set; } = true;
    public volatile bool Present = true;
    public IdentifyResult? AlwaysReturn { get; set; }
    public int ErrorsRemaining;
    public int IgnoreCancels { get; set; }
    public volatile bool BlockedInIdentify;
    /// <summary>Runs at the start of the n-th Identify call (1-based); a non-null result is returned instead of blocking.</summary>
    public Func<int, IdentifyResult?>? OnIdentify { get; set; }

    public IReadOnlyList<string> Calls => [.. calls];
    public int Count(string call) => calls.Count(c => c == call);

    public void Push(IdentifyResult r) => results.Add(r);

    public int AcquireFocus() { calls.Enqueue("AcquireFocus"); return AcquireHr; }
    public int ReleaseFocus() { calls.Enqueue("ReleaseFocus"); return ReleaseHr; }
    public bool Open() { calls.Enqueue("Open"); return OpenSucceeds; }
    public bool IsPresent() => Present;

    public IdentifyResult Identify()
    {
        calls.Enqueue("Identify");
        if (OnIdentify?.Invoke(Interlocked.Increment(ref identifyCalls)) is { } scripted) return scripted;
        if (AlwaysReturn is { } r) return r;
        if (Interlocked.Decrement(ref ErrorsRemaining) >= 0) return IdentifyResult.DeviceError;
        BlockedInIdentify = true;
        var result = results.Take();
        BlockedInIdentify = false;
        return result;
    }

    /// <summary>Like WinBioCancel: a cancel only reaches an Identify that is already blocked; an early one is lost.</summary>
    public void Cancel()
    {
        calls.Enqueue("Cancel");
        if (IgnoreCancels > 0) { IgnoreCancels--; return; }
        if (BlockedInIdentify) results.Add(IdentifyResult.Canceled);
    }

    public void Close() => calls.Enqueue("Close");
    public void Dispose() => calls.Enqueue("Dispose");
}
