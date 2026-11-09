using System.Diagnostics;
using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

public sealed class ScreenshotTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "biotap-shot-" + Guid.NewGuid().ToString("N"));
    readonly List<string> junctions = [];
    static readonly DateTime T = new(2026, 11, 9, 14, 30, 5);

    public ScreenshotTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        foreach (var j in junctions) try { Directory.Delete(j); } catch { }
        try { Directory.Delete(dir, true); } catch { }
    }

    string Junction(string target)
    {
        var link = Path.Combine(dir, "link-" + Guid.NewGuid().ToString("N")[..6]);
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        junctions.Add(link);
        return link;
    }

    static JsonObject P(string target = "screen", bool save = true, string folder = @"C:\Fotos", bool copy = true)
        => new() { ["target"] = target, ["save"] = save, ["folder"] = folder, ["copy"] = copy };

    static (ScreenshotPreset, FakeScreenCapture, FakeWindows) Make()
    {
        var cap = new FakeScreenCapture(); var win = new FakeWindows { ForegroundWindow = 77 };
        return (new ScreenshotPreset(cap, win, () => T), cap, win);
    }

    [Fact]
    public void Save_NamesByTime_AndNeverOverwrites()
    {
        var first = ScreenshotSaver.Save(dir, [1], T);
        var second = ScreenshotSaver.Save(dir, [2], T);
        Assert.Equal("BioTap 2026-11-09 14-30-05.png", Path.GetFileName(first));
        Assert.Equal("BioTap 2026-11-09 14-30-05 (2).png", Path.GetFileName(second));
        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
    }

    [Fact]
    public void CheckFolder_Missing() => Assert.Equal("Pasta não encontrada", ScreenshotSaver.CheckFolder(Path.Combine(dir, "nope")));

    [Fact]
    public void CheckFolder_Ok() => Assert.Null(ScreenshotSaver.CheckFolder(dir));

    [Fact]
    public void CheckFolder_Junction_IsInvalid()
    {
        var target = Directory.CreateDirectory(Path.Combine(dir, "real")).FullName;
        var link = Junction(target);
        Assert.True(Directory.Exists(link));
        Assert.Equal("Pasta inválida", ScreenshotSaver.CheckFolder(link));
    }

    [Fact]
    public void Save_ThroughJunction_IsRefused_AndWritesNothing()
    {
        var target = Directory.CreateDirectory(Path.Combine(dir, "real2")).FullName;
        var link = Junction(target);
        var ex = Assert.Throws<PresetException>(() => ScreenshotSaver.Save(link, [1], T));
        Assert.Equal("Pasta inválida", ex.Message);
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public void Save_ThroughJunctionInParent_IsRefused_AndDeletesWhatWasCreated()
    {
        var target = Directory.CreateDirectory(Path.Combine(dir, "real3")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        var link = Junction(target);
        var ex = Assert.Throws<PresetException>(() => ScreenshotSaver.Save(Path.Combine(link, "sub"), [1], T));
        Assert.Equal("Pasta inválida", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(target, "sub")));
    }

    [Fact]
    public void IsInside_NeedsTrailingSeparator()
    {
        Assert.True(ScreenshotSaver.IsInside(@"\\?\C:\Fotos\a.png", @"C:\Fotos"));
        Assert.True(ScreenshotSaver.IsInside(@"c:\FOTOS\a.png", @"C:\Fotos\"));
        Assert.False(ScreenshotSaver.IsInside(@"\\?\C:\Fotos2\a.png", @"C:\Fotos"));
    }

    [Theory]
    [InlineData("monitor", true, true, @"C:\Fotos", "Escolha Tela ou Janela atual.")]
    [InlineData("screen", false, false, @"C:\Fotos", "Marque Salvar, Copiar ou os dois.")]
    [InlineData("screen", true, true, "", "Escolha uma pasta (caminho completo).")]
    [InlineData("screen", true, true, @"Fotos\x", "Escolha uma pasta (caminho completo).")]
    [InlineData("screen", true, true, @"\\servidor\share", "Escolha uma pasta (caminho completo).")]
    [InlineData("window", true, false, @"C:\Fotos", null)]
    [InlineData("screen", false, true, "", null)]
    public void Validate(string target, bool save, bool copy, string folder, string? expected)
        => Assert.Equal(expected, Make().Item1.ValidateParams(P(target, save, folder, copy)));

    [Fact]
    public void CheckReady_WindowWithoutForeground()
    {
        var (preset, _, win) = Make();
        win.ForegroundWindow = 0;
        Assert.Equal("Nenhuma janela em foco", preset.CheckReady(P("window", false, copy: true)));
        Assert.Null(preset.CheckReady(P("screen", false, copy: true)));
    }

    [Fact]
    public void CheckReady_SaveChecksFolder()
        => Assert.Equal("Pasta não encontrada", Make().Item1.CheckReady(P(folder: Path.Combine(dir, "nope"))));

    [Fact]
    public async Task Run_CopyOnly_DoesNotTouchDisk()
    {
        var (preset, cap, _) = Make();
        await preset.RunAsync(P(save: false, folder: Path.Combine(dir, "nope")), default);
        Assert.Equal(["Monitor", "Copy:3"], cap.Calls);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public async Task Run_Window_CapturesForeground_AndSaves()
    {
        var (preset, cap, _) = Make();
        await preset.RunAsync(P("window", copy: false, folder: dir), default);
        Assert.Equal(["Window(77)"], cap.Calls);
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(dir, "BioTap 2026-11-09 14-30-05.png")));
    }

    [Fact]
    public async Task Run_Window_Gone_Throws()
    {
        var (preset, cap, _) = Make();
        cap.WindowPng = null;
        await Assert.ThrowsAsync<PresetException>(() => preset.RunAsync(P("window", copy: false, folder: dir), default));
    }
}
