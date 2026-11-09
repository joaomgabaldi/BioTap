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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Move(tmp, path, overwrite: true); // a crash never leaves a half-written icon
        return new IconRef("exe", value);
    }

    public static void Remove(string dataFolder, Guid actionId)
    {
        if (PathFor(dataFolder, actionId.ToString("N")) is { } path && File.Exists(path)) File.Delete(path);
    }
}
