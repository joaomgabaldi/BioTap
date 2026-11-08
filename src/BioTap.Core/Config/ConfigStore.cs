using System.Text.Json;
using System.Text.Json.Nodes;

namespace BioTap.Core.Config;

/// <param name="ReadOnly">The file could not be trusted or understood: show <see cref="Errors"/> and never overwrite it.</param>
public sealed record LoadResult(AppConfig Config, bool ReadOnly, IReadOnlyList<string> Errors, IReadOnlySet<Guid> InvalidActions);

public static class ConfigStore
{
    const long MaxFileBytes = 1024 * 1024;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
    };

    public static string Serialize(AppConfig c) => JsonSerializer.Serialize(c, Json);

    /// <summary>Never throws. Problems that make the file untrustworthy return ReadOnly; a bad action is only marked invalid.</summary>
    public static LoadResult Load(string path, Func<string, bool> isSecure, IPresetCatalog presets)
    {
        try
        {
            // Hold the file open denying writes while checking and reading it (same pattern as ServiceConfig)
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!isSecure(path)) return Broken("A configuração pode ser alterada por usuários sem admin; ela foi ignorada.");
            if (fs.Length > MaxFileBytes) return Broken("A configuração passa de 1 MB.");

            var node = JsonNode.Parse(fs, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
            if (node?["version"] is not JsonValue v || !v.TryGetValue<int>(out var version))
                return Broken("A configuração não tem versão.");
            if (version != AppConfig.CurrentVersion)
                return Broken($"Configuração da versão {version} não é suportada por este BioTap.");

            var config = node.Deserialize<AppConfig>(Json);
            if (config?.Overlay is null || config.Actions is null || config.Actions.Any(a => a?.Params is null || a.Icon is null || a.Name is null || a.Preset is null))
                return Broken("A configuração está incompleta.");

            var errors = ConfigValidator.Validate(config, presets);
            var global = errors.Where(e => e.ActionId is null).Select(e => e.Message).ToList();
            var invalid = errors.Where(e => e.ActionId is not null).Select(e => e.ActionId!.Value).ToHashSet();
            return new LoadResult(config, global.Count > 0, [.. global, .. errors.Where(e => e.ActionId is not null).Select(e => e.Message)], invalid);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LoadResult(AppConfig.Default, false, [], new HashSet<Guid>());
        }
        catch (Exception e)
        {
            return Broken($"A configuração não pôde ser lida ({e.GetType().Name}).");
        }
    }

    static LoadResult Broken(string error) => new(AppConfig.Default, true, [error], new HashSet<Guid>());

    /// <summary>Validates, then writes atomically (temp file in the same folder + replace). Throws if invalid.</summary>
    public static void Save(string path, AppConfig config, IPresetCatalog presets)
    {
        var errors = ConfigValidator.Validate(config, presets);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors.Select(e => e.Message)));

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(config)); // inherits the folder ACL (admin-only)
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}
