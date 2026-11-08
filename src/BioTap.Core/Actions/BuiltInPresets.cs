using System.Text;
using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>Starts programs. Spec §3: programs, files and URLs never start elevated; only "Rodar comando" may.</summary>
public interface ILauncher
{
    /// <summary>Through the (non-elevated) Explorer, like a double click. <paramref name="args"/> is passed verbatim.</summary>
    void OpenUnelevated(string file, string? args, string? workDir, bool showWindow = true);
    /// <summary>Inherits this app's elevation. Only for "Rodar comando" with runAsAdmin.</summary>
    void RunElevated(string file, string args, bool showWindow);
}

static class Params
{
    public static string? Str(JsonObject p, string name)
        => p[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool Bool(JsonObject p, string name, bool fallback)
        => p[name] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}

/// <summary>openTarget: { "target": absolute path | http(s) / ms-* URI, "args": string? }.</summary>
public sealed class OpenTargetPreset(ILauncher launcher) : IPreset
{
    const int MaxLength = 2048;

    public string Id => "openTarget";
    public string DisplayName => "Abrir programa, arquivo ou URL";
    public IconRef DefaultIcon => new("glyph", "E8E5");

    public string? ValidateParams(JsonObject p)
    {
        var target = Params.Str(p, "target");
        if (string.IsNullOrWhiteSpace(target)) return "Escolha o que abrir.";
        if (target.Length > MaxLength) return "Caminho longo demais.";
        if (Params.Str(p, "args") is { } args && (args.Length > MaxLength || args.Contains('\n') || args.Contains('\r')))
            return "Argumentos inválidos.";
        if (IsAllowedUri(target)) return null;
        var path = Environment.ExpandEnvironmentVariables(target);
        // Network paths are refused: they can hang and send the Windows credentials to another machine
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return "Caminhos de rede não são aceitos.";
        return Path.IsPathFullyQualified(path) ? null : "Use um caminho completo (ex.: C:\\...) ou uma URL http(s).";
    }

    public string? CheckReady(JsonObject p)
    {
        var target = Params.Str(p, "target")!;
        if (IsAllowedUri(target)) return null;
        var path = Environment.ExpandEnvironmentVariables(target);
        return File.Exists(path) || Directory.Exists(path) ? null : "Arquivo não encontrado";
    }

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        var target = Params.Str(p, "target")!;
        var file = IsAllowedUri(target) ? target : Environment.ExpandEnvironmentVariables(target);
        launcher.OpenUnelevated(file, Params.Str(p, "args"), null);
        return Task.CompletedTask;
    }

    static bool IsAllowedUri(string s)
        => !Path.IsPathFullyQualified(s)
           && Uri.TryCreate(s, UriKind.Absolute, out var u)
           && (u.Scheme is "http" or "https" || u.Scheme.StartsWith("ms-", StringComparison.Ordinal));
}

/// <summary>runCommand: { "shell": "powershell"|"cmd", "command": string, "showWindow": bool, "runAsAdmin": bool }.</summary>
public sealed class RunCommandPreset(ILauncher launcher) : IPreset
{
    const int MaxCommandLength = 8000;

    public string Id => "runCommand";
    public string DisplayName => "Rodar comando";
    public IconRef DefaultIcon => new("glyph", "E756");

    public string? ValidateParams(JsonObject p)
    {
        var shell = Params.Str(p, "shell");
        if (shell is not ("powershell" or "cmd")) return "Escolha PowerShell ou cmd.";
        var command = Params.Str(p, "command");
        if (string.IsNullOrWhiteSpace(command)) return "Escreva o comando.";
        if (command.Length > MaxCommandLength) return "Comando longo demais.";
        // cmd /c takes a single line; a newline would start a second, unseen command
        if (shell == "cmd" && (command.Contains('\n') || command.Contains('\r'))) return "No cmd, o comando precisa ter uma linha só.";
        return null;
    }

    public string? CheckReady(JsonObject p) => null;

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        var command = Params.Str(p, "command")!;
        var showWindow = Params.Bool(p, "showWindow", true);
        var system = Environment.SystemDirectory;
        var (file, args) = Params.Str(p, "shell") == "cmd"
            ? (Path.Combine(system, "cmd.exe"), "/d /c " + command)
            // -EncodedCommand: base64 of UTF-16, so quotes, ;, &, | and $ in the command are never re-parsed
            : (Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe"),
               "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));

        if (Params.Bool(p, "runAsAdmin", false)) launcher.RunElevated(file, args, showWindow);
        else launcher.OpenUnelevated(file, args, null, showWindow);
        return Task.CompletedTask;
    }
}
