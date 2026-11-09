using System.Globalization;
using System.Runtime.InteropServices;
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

    public static string Save(string folder, byte[] png, DateTime now) => Save(folder, png, now, (fs, bytes) => fs.Write(bytes));

    /// <summary><paramref name="write"/> is the seam that lets tests make the write fail.</summary>
    internal static string Save(string folder, byte[] png, DateTime now, Action<FileStream, byte[]> write)
    {
        if (CheckFolder(folder) is { } problem) throw new PresetException(problem);
        var full = Path.GetFullPath(folder);
        var stem = "BioTap " + now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        for (int n = 1; n < 1000; n++)
        {
            var path = Path.Combine(full, n == 1 ? stem + ".png" : $"{stem} ({n}).png");
            // CreateFile with DELETE access (FileStream cannot ask for it) so a failed save removes the file through the handle
            var handle = PInvoke.CreateFile(path, (uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE | FILE_ACCESS_RIGHTS.DELETE), FILE_SHARE_MODE.FILE_SHARE_NONE,
                null, FILE_CREATION_DISPOSITION.CREATE_NEW, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                if (File.Exists(path)) continue;
                throw new PresetException("Não foi possível salvar a imagem");
            }
            var fs = new FileStream(handle, FileAccess.Write, bufferSize: 0); // unbuffered: Dispose cannot fail on a pending write and replace the PresetException
            using (fs)
            {
                try
                {
                    if (!IsInside(FinalPath(fs), full)) throw new PresetException("Pasta inválida");
                    write(fs, png);
                    fs.Flush();
                    return path;
                }
                catch (Exception e) when (e is PresetException or IOException or UnauthorizedAccessException)
                {
                    // Nothing is deleted by name (the folder may have been swapped): the open handle marks it for deletion
                    try { SetDelete(fs); } catch (IOException) { /* best effort */ }
                    throw e is PresetException ? e : new PresetException("Não foi possível salvar a imagem");
                }
            }
        }
        throw new PresetException("Não foi possível salvar a imagem");
    }

    static void SetDelete(FileStream fs)
    {
        var info = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!PInvoke.SetFileInformationByHandle(fs.SafeFileHandle, FILE_INFO_BY_HANDLE_CLASS.FileDispositionInfo, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref info, 1))))
            throw new IOException();
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
public sealed class ScreenshotPreset(IScreenCapture capture, IWindows windows, Func<DateTime> now, string? defaultFolder = null) : IPreset
{
    /// <summary>The folder Windows shows as "Capturas de Tela".</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    readonly string defaultDir = defaultFolder ?? DefaultFolder;

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
        if (!Save(p)) return null;
        var folder = Params.Str(p, "folder")!;
        EnsureDefaultFolder(folder);
        return ScreenshotSaver.CheckFolder(folder);
    }

    /// <summary>Only the default folder is created, and only inside an existing, ordinary Pictures folder.</summary>
    void EnsureDefaultFolder(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (Directory.Exists(full) || !string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultDir)), StringComparison.OrdinalIgnoreCase)) return;
            var parent = Path.GetDirectoryName(full);
            if (parent is null || ScreenshotSaver.CheckFolder(parent) is not null) return;
            Directory.CreateDirectory(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { /* CheckFolder reports it */ }
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

        // Copy first: a failed save must not hide a successful copy
        if (Copy(p)) capture.CopyImage(png);
        if (Save(p)) ScreenshotSaver.Save(Params.Str(p, "folder")!, png, now());
        return Task.CompletedTask;
    }
}
