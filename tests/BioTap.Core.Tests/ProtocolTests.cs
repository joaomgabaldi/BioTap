namespace BioTap.Core.Tests;

public class ProtocolTests
{
    public static TheoryData<Message> AllMessages => new()
    {
        new TapsMessage(3),
        new StatusMessage(SensorState.Ok),
        new StatusMessage(SensorState.NoSensor),
        new StatusMessage(SensorState.NoFocus),
        new StatusMessage(SensorState.ReleasedForHello),
        new SetTimeoutMessage(800),
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void RoundTrip_AllMessageTypes(Message m)
    {
        var line = Protocol.Serialize(m);
        Assert.DoesNotContain('\n', line);
        Assert.True(Protocol.TryParse(line, out var parsed));
        Assert.Equal(m, parsed);
    }

    [Fact]
    public void Serialize_UsesWireFormat()
    {
        Assert.Equal("""{"type":"taps","n":3}""", Protocol.Serialize(new TapsMessage(3)));
        Assert.Equal("""{"type":"status","state":"releasedForHello"}""", Protocol.Serialize(new StatusMessage(SensorState.ReleasedForHello)));
        Assert.Equal("""{"type":"setTimeout","ms":800}""", Protocol.Serialize(new SetTimeoutMessage(800)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"type":"taps","n":0}""")]
    [InlineData("""{"type":"taps","n":-1}""")]
    [InlineData("""{"type":"taps"}""")]
    [InlineData("""{"type":"x"}""")]
    [InlineData("""{"type":"setTimeout","ms":299}""")]
    [InlineData("""{"type":"setTimeout","ms":3001}""")]
    [InlineData("""{"type":"status","state":"lol"}""")]
    [InlineData("""{"type":"taps","n":"3"}""")]
    [InlineData("""{"type":"taps","n":2.5}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"n":3}""")]
    public void TryParse_Rejects(string line)
    {
        Assert.False(Protocol.TryParse(line, out var m));
        Assert.Null(m);
    }

    [Fact]
    public void TryParse_RejectsLineOver1024Bytes()
    {
        var line = "{\"type\":\"taps\",\"n\":3,\"pad\":\"" + new string('a', 1024) + "\"}";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) > Protocol.MaxLineBytes);
        Assert.False(Protocol.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_IgnoresExtraFields()
    {
        Assert.True(Protocol.TryParse("""{"type":"taps","n":2,"cmd":"x"}""", out var m));
        Assert.Equal(new TapsMessage(2), m);
    }
}
