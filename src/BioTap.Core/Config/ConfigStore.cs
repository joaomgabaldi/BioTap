using System.Text.Json;
using System.Text.Json.Nodes;
using BioTap.Core.Text;

namespace BioTap.Core.Config;

/// <param name="ReadOnly">The file could not be trusted or understood, or has invalid global settings: show
/// <see cref="Errors"/> and never overwrite it. Actions in <see cref="InvalidActions"/> come back disabled.</param>
public sealed record LoadResult(AppConfig Config, bool ReadOnly, IReadOnlyList<string> Errors, IReadOnlySet<Guid> InvalidActions);

public static class ConfigStore
{
    const long MaxFileBytes = 1024 * 1024;
    const int ReplaceAttempts = 20; // × 50 ms: readers (app, service) hold the file only briefly

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        RespectNullableAnnotations = true, // a null in a non-nullable property is an error, not a silent null
    };

    public static string Serialize(AppConfig c) => JsonSerializer.Serialize(c, Json);

    /// <summary>Never throws. See <see cref="LoadResult"/>.</summary>
    public static LoadResult Load(string path, Func<string, bool> isSecure, IPresetCatalog presets)
    {
        try
        {
            // Hold the file open denying writes while checking and reading it (same pattern as ServiceConfig)
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!isSecure(path)) return Broken(Strings.Current.ConfigInsecure);
            if (fs.Length > MaxFileBytes) return Broken(Strings.Current.ConfigTooBig);

            var node = JsonNode.Parse(fs, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
            if (node is not JsonObject root || root["version"] is not JsonValue v || !v.TryGetValue<int>(out var version))
                return Broken(Strings.Current.ConfigNoVersion);
            if (version != AppConfig.CurrentVersion)
                return Broken(Strings.Current.ConfigVersionUnsupported(version));

            var config = root.Deserialize<AppConfig>(Json);
            if (config?.Overlay is null || config.Actions is null || config.Actions.Any(IsIncomplete))
                return Broken(Strings.Current.ConfigIncomplete);

            var errors = ConfigValidator.Validate(config, presets);
            var invalid = errors.Where(e => e.ActionId is not null).Select(e => e.ActionId!.Value).ToHashSet();
            var global = errors.Where(e => e.ActionId is null).Select(e => e.Message).ToList();
            // invalid actions come back disabled: they never run and never block saving the rest
            config = config with { Actions = config.Actions.Select(a => invalid.Contains(a.Id) ? a with { Enabled = false } : a).ToList() };
            return new LoadResult(config, global.Count > 0, [.. global, .. errors.Where(e => e.ActionId is not null).Select(e => e.Message)], invalid);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LoadResult(AppConfig.Default, false, [], new HashSet<Guid>());
        }
        catch (Exception e)
        {
            return Broken(Strings.Current.ConfigUnreadable(e.GetType().Name));
        }
    }

    static bool IsIncomplete(ActionConfig? a)
        => a?.Params is null || a.Icon?.Kind is null || a.Icon.Value is null || a.Name is null || a.Preset is null;

    static LoadResult Broken(string error) => new(AppConfig.Default, true, [error], new HashSet<Guid>());

    /// <summary>
    /// Refuses to overwrite a file that does not load cleanly (untrusted, unreadable, future version), validates,
    /// then writes atomically: temp file in the same folder, flushed to disk, then replace (keeps the old file's ACL).
    /// </summary>
    public static void Save(string path, AppConfig config, IPresetCatalog presets, Func<string, bool> isSecure)
    {
        if (File.Exists(path) && Load(path, isSecure, presets) is { ReadOnly: true } existing)
            throw new InvalidOperationException(Strings.Current.ConfigCannotOverwrite + string.Join(" ", existing.Errors));
        var errors = ConfigValidator.Validate(config, presets);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors.Select(e => e.Message)));

        var tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(fs, config, Json);
                fs.Flush(flushToDisk: true); // never replace a good file with one that did not reach the disk
            }
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
                    else File.Move(tmp, path);
                    return;
                }
                catch (IOException) when (attempt < ReplaceAttempts)
                {
                    Thread.Sleep(50); // a reader (app or service) holds the file for a moment
                }
            }
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }
}
