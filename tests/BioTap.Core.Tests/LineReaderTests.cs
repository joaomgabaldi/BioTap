using System.Text;

namespace BioTap.Core.Tests;

public class LineReaderTests
{
    static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    /// <summary>Returns at most one byte per read, to exercise lines split across reads.</summary>
    sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
    }

    [Fact]
    public async Task TwoLinesInOneChunk_ReturnsBoth()
    {
        var r = new LineReader(Bytes("a\nb\n"));
        Assert.Equal((LineStatus.Line, "a"), await r.ReadLineAsync(default));
        Assert.Equal((LineStatus.Line, "b"), await r.ReadLineAsync(default));
        Assert.Equal((LineStatus.EndOfStream, null), await r.ReadLineAsync(default));
    }

    [Fact]
    public async Task LineSplitAcrossReads_IsJoined()
    {
        var r = new LineReader(new TrickleStream(Encoding.UTF8.GetBytes("{\"type\":\"taps\",\"n\":2}\n")));
        Assert.Equal((LineStatus.Line, "{\"type\":\"taps\",\"n\":2}"), await r.ReadLineAsync(default));
    }

    [Fact]
    public async Task ExactlyMaxBytes_IsALine()
    {
        var r = new LineReader(Bytes(new string('a', Protocol.MaxLineBytes) + "\n"));
        var (status, line) = await r.ReadLineAsync(default);
        Assert.Equal(LineStatus.Line, status);
        Assert.Equal(Protocol.MaxLineBytes, line!.Length);
    }

    [Fact]
    public async Task OverMaxBytes_IsTooLong()
    {
        var r = new LineReader(Bytes(new string('a', Protocol.MaxLineBytes + 1) + "\n"));
        Assert.Equal(LineStatus.TooLong, (await r.ReadLineAsync(default)).Status);
    }

    [Fact]
    public async Task OverMaxWithoutNewline_IsTooLong()
    {
        var r = new LineReader(Bytes(new string('a', 10_000)));
        Assert.Equal(LineStatus.TooLong, (await r.ReadLineAsync(default)).Status);
    }

    [Fact]
    public async Task PartialLineAtEnd_IsDiscarded()
    {
        var r = new LineReader(Bytes("a\npartial"));
        Assert.Equal((LineStatus.Line, "a"), await r.ReadLineAsync(default));
        Assert.Equal((LineStatus.EndOfStream, null), await r.ReadLineAsync(default));
    }

    [Fact]
    public async Task InvalidUtf8_DecodesWithReplacementInsteadOfThrowing()
    {
        var r = new LineReader(new MemoryStream([0x61, 0xFF, 0x62, (byte)'\n']));
        Assert.Equal((LineStatus.Line, "a�b"), await r.ReadLineAsync(default));
    }

    [Fact]
    public async Task CarriageReturn_IsStripped()
    {
        var r = new LineReader(Bytes("a\r\n"));
        Assert.Equal((LineStatus.Line, "a"), await r.ReadLineAsync(default));
    }
}
