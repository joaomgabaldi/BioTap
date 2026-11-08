using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

public class PresetReviewTests
{
    readonly FakeLauncher launcher = new();

    [Theory]
    [InlineData("//srv/share/x.exe")]
    [InlineData(@"\\?\UNC\srv\share\x.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("ms-appinstaller:?source=http://x/a.appinstaller")]
    [InlineData("ms-officecmd:x")]
    [InlineData(@"C:\x" + "\0" + ".exe")]
    public void OpenTarget_Rejects(string target)
        => Assert.NotNull(new OpenTargetPreset(launcher).ValidateParams(new JsonObject { ["target"] = target }));

    [Theory]
    [InlineData("ms-settings:sound")]
    [InlineData("ms-screenclip:")]
    public void OpenTarget_AllowsKnownSettingsUris(string target)
        => Assert.Null(new OpenTargetPreset(launcher).ValidateParams(new JsonObject { ["target"] = target }));

    [Fact]
    public async Task RunCommand_Cmd_QuotesTheWholeCommandForSlashS()
    {
        const string command = @"""C:\Program Files\x.exe"" ""a b""";
        await new RunCommandPreset(launcher).RunAsync(new JsonObject { ["shell"] = "cmd", ["command"] = command }, default);
        Assert.Equal("/d /s /c \"" + command + "\"", launcher.Calls.Single().Args);
    }

    [Theory]
    [InlineData("dir\0 x")]
    [InlineData("dir\u0007")]
    public void RunCommand_Cmd_RejectsControlCharacters(string command)
        => Assert.NotNull(new RunCommandPreset(launcher).ValidateParams(new JsonObject { ["shell"] = "cmd", ["command"] = command }));

    [Fact]
    public async Task RunCommand_PowerShell_IsNonInteractive()
    {
        await new RunCommandPreset(launcher).RunAsync(new JsonObject { ["shell"] = "powershell", ["command"] = "x" }, default);
        Assert.StartsWith("-NoProfile -NonInteractive -EncodedCommand ", launcher.Calls.Single().Args);
    }

    // The process that will start the program must be the user's normal, non-elevated Explorer
    [Theory]
    [InlineData(false, @"C:\Windows\explorer.exe", null)]
    [InlineData(true, @"C:\Windows\explorer.exe", "elevated")]
    [InlineData(false, @"C:\Windows\System32\rundll32.exe", "not explorer")]
    [InlineData(false, null, "unknown")]
    public void ShellHostCheck(bool elevated, string? image, string? expectedProblem)
    {
        var problem = ShellLauncher.CheckShellHost(elevated, image, @"C:\Windows");
        if (expectedProblem is null) Assert.Null(problem);
        else Assert.NotNull(problem);
    }
}
