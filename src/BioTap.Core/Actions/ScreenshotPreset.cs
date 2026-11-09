using System.Globalization;
using System.Text.Json.Nodes;
using BioTap.Core.Config;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace BioTap.Core.Actions;

/// <summary>Screen capture (Win32/WinRT in the App; a fake in tests). PNG bytes.</summary>
public interface IScreenCapture
{
    byte[] CaptureMonitorUnderMouse();
    /// <summary>Null when the window is gone.</summary>
    byte[]? CaptureWindow(nint window);
    void CopyImage(byte[] png);
}

/// <summary>
/// Writes screenshots. The app is elevated and the folder is user-writable, so this is the last decision point:
/// reparse-point folders are refused, files are only ever created (never overwritten) and the opened file's real path
/// must be inside the chosen folder (a junction higher up would otherwise redirect an elevated write).
/// </summary>
public static class ScreenshotSaver
{
    public static string? CheckFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return "Pasta não encontrada";
            return new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint) ? "Pasta inválida" : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "Pasta inválida";
        }
    }

    public static string Save(string folder, byte[] png, DateTime now)
    {
        if (CheckFolder(folder) is { } problem) throw new PresetException(problem);
        var full = Path.GetFullPath(folder);
        var stem = "BioTap " + now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        for (int n = 1; n < 1000; n++)
        {
            var path = Path.Combine(full, n == 1 ? stem + ".png" : $"{stem} ({n}).png");
            FileStream fs;
            try { fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new PresetException("Não foi possível salvar a imagem"); }

            var ok = false;
            try
            {
                using (fs)
                {
                    if (!IsInside(FinalPath(fs), full)) throw new PresetException("Pasta inválida");
                    fs.Write(png);
                }
                ok = true;
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new PresetException("Não foi possível salvar a imagem"); }
            finally { if (!ok) try { File.Delete(path); } catch { } } // the stream is closed by now
        }
        throw new PresetException("Não foi possível salvar a imagem");
    }

    static string FinalPath(FileStream fs)
    {
        var buf = new char[32768];
        var len = PInvoke.GetFinalPathNameByHandle(fs.SafeFileHandle, buf, GETFINALPATHNAMEBYHANDLE_FLAGS.FILE_NAME_NORMALIZED);
        if (len == 0 || len >= buf.Length) throw new PresetException("Pasta inválida");
        return new string(buf, 0, (int)len);
    }

    internal static bool IsInside(string finalPath, string folder)
    {
        const string Prefix = @"\\?\";
        if (finalPath.StartsWith(Prefix, StringComparison.Ordinal)) finalPath = finalPath[Prefix.Length..];
        var root = folder.TrimEnd('\\', '/') + "\\";
        return finalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>screenshot: { "target": "screen"|"window", "save": bool, "folder": string, "copy": bool }.</summary>
public sealed class ScreenshotPreset(IScreenCapture capture, IWindows windows, Func<DateTime> now) : IPreset
{
    public string Id => "screenshot";
    public string DisplayName => "Captura de tela";
    public IconRef DefaultIcon => new("glyph", "E722");

    // Missing fields mean the defaults (the editor writes them all, so a saved action is complete)
    static string Target(JsonObject p) => Params.Str(p, "target") ?? "screen";
    static bool Save(JsonObject p) => Params.Bool(p, "save", true);
    static bool Copy(JsonObject p) => Params.Bool(p, "copy", true);

    public string? ValidateParams(JsonObject p)
    {
        if (Target(p) is not ("screen" or "window")) return "Escolha Tela ou Janela atual.";
        if (!Save(p) && !Copy(p)) return "Marque Salvar, Copiar ou os dois.";
        if (Save(p))
        {
            var f = Params.Str(p, "folder");
            if (string.IsNullOrWhiteSpace(f) || f.Any(char.IsControl) || f.StartsWith(@"\\", StringComparison.Ordinal)
                || f.StartsWith("//", StringComparison.Ordinal) || !Path.IsPathFullyQualified(f))
                return "Escolha uma pasta (caminho completo).";
        }
        return null;
    }

    public string? CheckReady(JsonObject p)
    {
        if (Target(p) == "window" && windows.Foreground() == 0) return "Nenhuma janela em foco";
        return Save(p) ? ScreenshotSaver.CheckFolder(Params.Str(p, "folder")!) : null;
    }

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        byte[] png;
        if (Target(p) == "window")
        {
            var h = windows.Foreground();
            if (h == 0) throw new PresetException("Nenhuma janela em foco");
            png = capture.CaptureWindow(h) ?? throw new PresetException("Janela não encontrada");
        }
        else png = capture.CaptureMonitorUnderMouse();

        if (Save(p)) ScreenshotSaver.Save(Params.Str(p, "folder")!, png, now());
        if (Copy(p)) capture.CopyImage(png);
        return Task.CompletedTask;
    }
}
