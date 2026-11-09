namespace BioTap.Core.Config;

public interface IIconExtractor { byte[]? ExtractPng(string file, int size); }

/// <summary>Program icons cached as ProgramData\BioTap\icons\&lt;id:N&gt;.png (spec §5). PathFor is the only way to build a path from config.</summary>
public static class IconCache
{
    /// <summary>The cached file for an "exe" icon value, or null unless the value is exactly 32 hex digits (config is hand-editable).</summary>
    public static string? PathFor(string dataFolder, string value)
        => Guid.TryParseExact(value, "N", out _) ? Path.Combine(dataFolder, "icons", value + ".png") : null;

    /// <summary>Saves the PNG and returns the exe icon; with no PNG nothing is written and <paramref name="fallback"/> is returned.</summary>
    public static IconRef Store(string dataFolder, Guid actionId, byte[]? png, IconRef fallback)
    {
        var value = actionId.ToString("N");
        if (png is null || PathFor(dataFolder, value) is not { } path) return fallback;
        var tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(tmp, png);
            File.Move(tmp, path, overwrite: true); // a crash never leaves a half-written icon
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception) { }
            return fallback;
        }
        return new IconRef("exe", value);
    }

    /// <summary>Whether the icon of this expanded path may be read: a local-drive path (X:\...) to an .exe, .dll or .ico. The elevated app must not run shell icon handlers (per-user registrable) for other types or UNC paths.</summary>
    public static bool CanExtractFrom(string expandedPath)
        => expandedPath.Length > 3 && char.IsAsciiLetter(expandedPath[0]) && expandedPath[1] == ':' && expandedPath[2] == '\\'
           && Path.GetExtension(expandedPath) is var ext
           && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase));

    public static void Remove(string dataFolder, Guid actionId)
    {
        if (PathFor(dataFolder, actionId.ToString("N")) is { } path && File.Exists(path)) File.Delete(path);
    }
}
