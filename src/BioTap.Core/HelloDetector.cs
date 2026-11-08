namespace BioTap.Core;

/// <param name="ImagePath">Full exe path, or null when it could not be read.</param>
public readonly record struct ProcessInfo(string Name, int SessionId, string? ImagePath);

public static class HelloDetector
{
    /// <summary>
    /// True when a Windows Hello prompt process runs in the console session from the Windows directory.
    /// The session check ignores prompts of other users (fast user switching, RDP); the path check ignores a user's
    /// lookalike exe. An unreadable path counts as Hello: when in doubt, leave the sensor to Hello.
    /// </summary>
    /// <param name="consoleSessionId">Active console session, or -1 when there is none (then any session counts).</param>
    public static bool IsHelloActive(IEnumerable<ProcessInfo> processes, int consoleSessionId, string windowsDir)
    {
        var root = Path.TrimEndingDirectorySeparator(windowsDir) + Path.DirectorySeparatorChar;
        foreach (var p in processes)
        {
            if (!FocusPolicy.HelloProcesses.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (consoleSessionId >= 0 && p.SessionId != consoleSessionId) continue;
            if (p.ImagePath is null || p.ImagePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
