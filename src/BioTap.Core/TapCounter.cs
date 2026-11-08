namespace BioTap.Core;

/// <summary>Counts touches and emits the count once no touch arrives for <see cref="TimeoutMs"/>.</summary>
public sealed class TapCounter : IDisposable
{
    public const int MinTimeoutMs = 300;
    public const int MaxTimeoutMs = 3000;

    readonly ITimer timer;
    readonly Lock gate = new();
    int count;
    int timeoutMs;

    public TapCounter(int timeoutMs, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        TimeoutMs = timeoutMs;
        timer = time.CreateTimer(_ => Complete(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event Action<int>? SequenceCompleted;

    public int TimeoutMs
    {
        get => Volatile.Read(ref timeoutMs);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinTimeoutMs);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTimeoutMs);
            Volatile.Write(ref timeoutMs, value);
        }
    }

    public void Tap()
    {
        Interlocked.Increment(ref count);
        timer.Change(TimeSpan.FromMilliseconds(TimeoutMs), Timeout.InfiniteTimeSpan);
    }

    void Complete()
    {
        // Timer callbacks can overlap; serialize so sequences are emitted one at a time, in order.
        lock (gate)
        {
            var n = Interlocked.Exchange(ref count, 0);
            if (n > 0) SequenceCompleted?.Invoke(n);
        }
    }

    public void Dispose() => timer.Dispose();
}
