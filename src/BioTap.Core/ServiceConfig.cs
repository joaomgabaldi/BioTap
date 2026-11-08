using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BioTap.Core;

/// <summary>The only part of config.json the service reads.</summary>
public sealed record ServiceConfig(int TapTimeoutMs)
{
    const long MaxFileBytes = 1024 * 1024;

    public static ServiceConfig Default { get; } = new(800);

    /// <summary>Never throws: any problem (missing, insecure ACL, bad JSON, out of range) falls back to <see cref="Default"/>.</summary>
    public static ServiceConfig Load(string path, Func<string, bool> isSecure, ILogger log)
    {
        try
        {
            // Hold the file open denying writes/deletes, so the ACL we check is the file we read.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!isSecure(path))
            {
                log.LogWarning("Config is writable by non-admins; ignoring it and using defaults");
                return Default;
            }
            if (fs.Length > MaxFileBytes)
            {
                log.LogWarning("Config larger than {Max} bytes; using defaults", MaxFileBytes);
                return Default;
            }
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("tapTimeoutMs", out var p)
                && p.ValueKind == JsonValueKind.Number
                && p.TryGetInt32(out var ms)
                && ms is >= TapCounter.MinTimeoutMs and <= TapCounter.MaxTimeoutMs)
                return new ServiceConfig(ms);
            log.LogWarning("Config tapTimeoutMs missing or out of range; using default");
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            log.LogInformation("Config not found, using defaults");
        }
        catch (Exception e)
        {
            log.LogWarning("Config unreadable ({Error}); using defaults", e.GetType().Name);
        }
        return Default;
    }
}
