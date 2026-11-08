using System.Text;

namespace BioTap.Core;

public enum LineStatus { Line, TooLong, EndOfStream }

/// <summary>Reads '\n'-terminated UTF-8 lines with a hard byte limit; never buffers more than one line.</summary>
public sealed class LineReader(Stream stream, int maxLineBytes = Protocol.MaxLineBytes)
{
    readonly byte[] buffer = new byte[maxLineBytes + 2]; // line + optional '\r' + '\n'
    int length;

    public async ValueTask<(LineStatus Status, string? Line)> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var nl = Array.IndexOf(buffer, (byte)'\n', 0, length);
            if (nl >= 0)
            {
                var end = nl > 0 && buffer[nl - 1] == (byte)'\r' ? nl - 1 : nl;
                if (end > maxLineBytes) return (LineStatus.TooLong, null);
                var line = Encoding.UTF8.GetString(buffer, 0, end); // invalid bytes become U+FFFD
                Buffer.BlockCopy(buffer, nl + 1, buffer, 0, length - nl - 1);
                length -= nl + 1;
                return (LineStatus.Line, line);
            }
            if (length == buffer.Length) return (LineStatus.TooLong, null);
            var n = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (n == 0) return (LineStatus.EndOfStream, null);
            length += n;
        }
    }
}
