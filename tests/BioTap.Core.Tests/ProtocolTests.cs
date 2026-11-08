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

    // Built inside the test: attribute and theory data are serialized as UTF-8, which turns lone surrogates into U+FFFD.
    [Fact]
    public void TryParse_LoneSurrogate_ReturnsFalseWithoutThrowing()
    {
        Assert.False(Protocol.TryParse("{\"type\":\"taps\",\"n\":2}" + (char)0xD800, out _));
        Assert.False(Protocol.TryParse("{\"type\":\"taps\",\"n\":2,\"x\":\"" + (char)0xDC00 + "\"}", out _));
    }

    [Fact]
    public void TryParse_MeasuresBytesNotChars()
    {
        // 600 two-byte chars: under 1024 chars, over 1024 bytes
        var line = "{\"type\":\"taps\",\"n\":3,\"pad\":\"" + new string('é', 600) + "\"}";
        Assert.True(line.Length < Protocol.MaxLineBytes);
        Assert.False(Protocol.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_ExactlyMaxBytes_Accepted()
    {
        var head = "{\"type\":\"taps\",\"n\":3,\"pad\":\"";
        var line = head + new string('a', Protocol.MaxLineBytes - head.Length - 2) + "\"}";
        Assert.Equal(Protocol.MaxLineBytes, line.Length);
        Assert.True(Protocol.TryParse(line, out _));
        Assert.False(Protocol.TryParse(line.Insert(head.Length, "a"), out _));
    }

    [Fact]
    public void TryParse_IgnoresExtraFields()
    {
        Assert.True(Protocol.TryParse("""{"type":"taps","n":2,"cmd":"x"}""", out var m));
        Assert.Equal(new TapsMessage(2), m);
    }
}
