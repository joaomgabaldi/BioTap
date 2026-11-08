using System.Text;
using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

sealed class FakeLauncher : ILauncher
{
    public readonly List<(string Kind, string File, string? Args, bool ShowWindow)> Calls = [];
    public void OpenUnelevated(string file, string? args, string? workDir, bool showWindow = true) => Calls.Add(("unelevated", file, args, showWindow));
    public void RunElevated(string file, string args, bool showWindow) => Calls.Add(("elevated", file, args, showWindow));
}

public sealed class PresetTests : IDisposable
{
    readonly FakeLauncher launcher = new();
    readonly string tmp = Path.Combine(Path.GetTempPath(), "biotap preset test " + Guid.NewGuid() + ".txt");
    public PresetTests() => File.WriteAllText(tmp, "x");
    public void Dispose() => File.Delete(tmp);

    OpenTargetPreset Open => new(launcher);
    RunCommandPreset Cmd => new(launcher);

    [Fact]
    public void OpenTarget_MissingFile_NotReady()
        => Assert.NotNull(Open.CheckReady(new JsonObject { ["target"] = @"C:\nao\existe\x.exe" }));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("ms-screenclip:")]
    [InlineData("ms-settings:sound")]
    public void OpenTarget_Uri_ValidAndReady(string uri)
    {
        var p = new JsonObject { ["target"] = uri };
        Assert.Null(Open.ValidateParams(p));
        Assert.Null(Open.CheckReady(p));
    }

    [Theory]
    [InlineData(@"relative\x.exe")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x.exe")]
    [InlineData(@"\\server\share\x.exe")]
    public void OpenTarget_RejectsRelativeEmptyAndUnsafeSchemes(string target)
        => Assert.NotNull(Open.ValidateParams(new JsonObject { ["target"] = target }));

    [Fact]
    public void OpenTarget_MissingTarget_Invalid() => Assert.NotNull(Open.ValidateParams(new JsonObject()));

    [Fact]
    public async Task OpenTarget_PathWithSpacesAndQuotes_PassedAsSingleArgument()
    {
        await Open.RunAsync(new JsonObject { ["target"] = tmp, ["args"] = "--flag \"a b\"" }, default);
        var call = Assert.Single(launcher.Calls);
        Assert.Equal(("unelevated", tmp, "--flag \"a b\""), (call.Kind, call.File, call.Args));
    }

    [Fact]
    public async Task OpenTarget_ExpandsEnvVars()
    {
        var p = new JsonObject { ["target"] = "%TEMP%\\" + Path.GetFileName(tmp) };
        Assert.Null(Open.CheckReady(p));
        await Open.RunAsync(p, default);
        Assert.Equal(Path.GetFullPath(tmp), Path.GetFullPath(launcher.Calls.Single().File), ignoreCase: true);
    }

    [Fact]
    public async Task RunCommand_Unelevated_ByDefault()
    {
        await Cmd.RunAsync(new JsonObject { ["shell"] = "powershell", ["command"] = "Get-Date" }, default);
        Assert.Equal("unelevated", launcher.Calls.Single().Kind);
    }

    [Fact]
    public async Task RunCommand_RunAsAdmin_UsesElevatedPath()
    {
        await Cmd.RunAsync(new JsonObject { ["shell"] = "powershell", ["command"] = "Get-Date", ["runAsAdmin"] = true }, default);
        Assert.Equal("elevated", launcher.Calls.Single().Kind);
    }

    [Fact]
    public async Task RunCommand_PowerShellCommand_IsEncoded()
    {
        const string command = "Write-Output \"it's; & | $env:X\"";
        await Cmd.RunAsync(new JsonObject { ["shell"] = "powershell", ["command"] = command }, default);
        var args = launcher.Calls.Single().Args!;
        Assert.DoesNotContain("Write-Output", args); // never concatenated
        var b64 = args.Split(' ').Last();
        Assert.Equal(command, Encoding.Unicode.GetString(Convert.FromBase64String(b64)));
        Assert.StartsWith("-NoProfile", args);
    }

    [Fact]
    public async Task RunCommand_Cmd_UsesSlashC()
    {
        await Cmd.RunAsync(new JsonObject { ["shell"] = "cmd", ["command"] = "dir" }, default);
        var c = launcher.Calls.Single();
        Assert.EndsWith("cmd.exe", c.File, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/d /c dir", c.Args);
    }

    [Theory]
    [InlineData("dir\nshutdown /s")]
    [InlineData("dir\r\necho x")]
    [InlineData("")]
    public void RunCommand_Cmd_RejectsNewlinesAndEmpty(string command)
        => Assert.NotNull(Cmd.ValidateParams(new JsonObject { ["shell"] = "cmd", ["command"] = command }));

    [Fact]
    public void RunCommand_UnknownShell_Invalid()
        => Assert.NotNull(Cmd.ValidateParams(new JsonObject { ["shell"] = "bash", ["command"] = "ls" }));

    [Fact]
    public void RunCommand_TooLong_Invalid()
        => Assert.NotNull(Cmd.ValidateParams(new JsonObject { ["shell"] = "powershell", ["command"] = new string('a', 20_000) }));

    [Fact]
    public async Task RunCommand_ShowWindowFalse_IsPassed()
    {
        await Cmd.RunAsync(new JsonObject { ["shell"] = "cmd", ["command"] = "dir", ["showWindow"] = false }, default);
        Assert.False(launcher.Calls.Single().ShowWindow);
    }
}
