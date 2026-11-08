using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

public class LimitsTests
{
    [Fact]
    public void TapCounter_TapAfterDispose_IsNoOp()
    {
        var c = new TapCounter(800, new FakeTimeProvider());
        c.Dispose();
        c.Tap(); // must not throw
    }

    [Fact]
    public void TapCounter_PendingSequenceAfterDispose_NeverEmits()
    {
        var time = new FakeTimeProvider();
        var c = new TapCounter(800, time);
        var emitted = 0;
        c.SequenceCompleted += _ => emitted++;
        c.Tap();
        c.Dispose();
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, emitted);
    }

    [Theory]
    [InlineData("""{"type":"taps","n":3,"n":5}""")]
    [InlineData("""{"type":"taps","type":"status","n":3}""")]
    public void Protocol_RejectsDuplicateKeys(string line) => Assert.False(Protocol.TryParse(line, out _));

    [Fact]
    public void Protocol_TapsUpperBound()
    {
        Assert.True(Protocol.TryParse("{\"type\":\"taps\",\"n\":" + Protocol.MaxTaps + "}", out _));
        Assert.False(Protocol.TryParse("{\"type\":\"taps\",\"n\":" + (Protocol.MaxTaps + 1) + "}", out _));
    }

    [Fact]
    public void Protocol_SerializeUnknownState_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => Protocol.Serialize(new StatusMessage((SensorState)99)));
}
