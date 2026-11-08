namespace BioTap.Core;

/// <summary>
/// Minimal rotating text log (spec §5: logs in ProgramData\BioTap\logs, 5 × 1 MB). Never throws.
/// Callers must not pass secrets (typed text, clipboard, command arguments).
/// </summary>
public sealed class FileLog(string folder, string name, long maxBytes = 1024 * 1024, int keep = 5)
{
    readonly Lock gate = new();
    string Current => Path.Combine(folder, name + ".log");

    public void Write(string level, string message)
    {
        // A1: neutralize line breaks so a message cannot forge extra log lines
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}";
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(Current) && new FileInfo(Current).Length + line.Length > maxBytes) Rotate();
                File.AppendAllText(Current, line);
            }
        }
        catch (Exception)
        {
            // logging must never take the app down
        }
    }

    void Rotate()
    {
        File.Delete(Path.Combine(folder, $"{name}.{keep - 1}.log"));
        for (var i = keep - 2; i >= 1; i--)
        {
            var from = Path.Combine(folder, $"{name}.{i}.log");
            if (File.Exists(from)) File.Move(from, Path.Combine(folder, $"{name}.{i + 1}.log"));
        }
        File.Move(Current, Path.Combine(folder, $"{name}.1.log"));
    }
}
