using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

public class TapCounterTests
{
    readonly FakeTimeProvider time = new();
    readonly List<int> emitted = [];

    TapCounter NewCounter(int timeoutMs = 800)
    {
        var c = new TapCounter(timeoutMs, time);
        c.SequenceCompleted += n => emitted.Add(n);
        return c;
    }

    void Advance(int ms) => time.Advance(TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void ThreeTaps_ThenTimeout_EmitsThree()
    {
        using var c = NewCounter();
        c.Tap(); Advance(200); c.Tap(); Advance(200); c.Tap();
        Advance(800);
        Assert.Equal([3], emitted);
    }

    [Fact]
    public void Tap_ExactlyAtTimeout_StartsNewSequence()
    {
        using var c = NewCounter();
        c.Tap(); Advance(800);
        c.Tap(); Advance(800);
        Assert.Equal([1, 1], emitted);
    }

    [Fact]
    public void Tap_JustBeforeTimeout_Extends()
    {
        using var c = NewCounter();
        c.Tap(); Advance(799);
        c.Tap(); Advance(800);
        Assert.Equal([2], emitted);
    }

    [Fact]
    public void FifteenTaps_EmitsFifteen()
    {
        using var c = NewCounter();
        for (var i = 0; i < 15; i++) { c.Tap(); Advance(300); }
        Advance(800);
        Assert.Equal([15], emitted);
    }

    [Fact]
    public void NoTaps_NeverEmits()
    {
        using var c = NewCounter();
        Advance(10_000);
        Assert.Empty(emitted);
    }

    [Theory]
    [InlineData(299)]
    [InlineData(3001)]
    public void TimeoutMs_OutOfRange_Throws(int ms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapCounter(ms, time));
        using var c = NewCounter();
        Assert.Throws<ArgumentOutOfRangeException>(() => c.TimeoutMs = ms);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(3000)]
    public void TimeoutMs_AtLimits_Accepted(int ms)
    {
        using var c = NewCounter();
        c.TimeoutMs = ms;
        Assert.Equal(ms, c.TimeoutMs);
    }

    [Fact]
    public void TimeoutChangedMidSequence_AppliesFromNextTap()
    {
        using var c = NewCounter();
        c.Tap();
        c.TimeoutMs = 2000;
        Advance(800);
        Assert.Equal([1], emitted);

        c.Tap();
        Advance(1999);
        Assert.Equal([1], emitted);
        Advance(1);
        Assert.Equal([1, 1], emitted);
    }

    [Fact]
    public async Task SequenceHandlers_NeverRunConcurrently()
    {
        // Real timer: a slow handler must not overlap the next sequence's handler.
        using var c = new TapCounter(TapCounter.MinTimeoutMs, TimeProvider.System);
        int active = 0, maxActive = 0;
        c.SequenceCompleted += _ =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            Thread.Sleep(600);
            Interlocked.Decrement(ref active);
        };
        c.Tap();
        await Task.Delay(400); // first handler running
        c.Tap();
        await Task.Delay(1500); // second sequence completes while/after the first
        Assert.Equal(1, maxActive);
    }

    static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
