using System.Diagnostics;
using System.Security.Principal;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

/// <summary>
/// Real launches. Meaningful only when the test process is elevated (run with: dotnet test --filter Category=Elevated
/// from an admin prompt); without elevation they return early, because nothing can be proven then.
/// </summary>
[Trait("Category", "Elevated")]
public class ShellLauncherElevatedTests
{
    static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static async Task<Process> WaitForNewPing(HashSet<int> before)
    {
        for (var i = 0; i < 100; i++)
        {
            var p = Process.GetProcessesByName("PING").FirstOrDefault(x => !before.Contains(x.Id));
            if (p is not null) return p;
            await Task.Delay(100);
        }
        throw new TimeoutException("ping.exe did not start");
    }

    static HashSet<int> PingIds() => Process.GetProcessesByName("PING").Select(p => p.Id).ToHashSet();
    static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task OpenUnelevated_FromAnElevatedProcess_StartsANonElevatedChild()
    {
        if (!IsAdmin) return;
        var before = PingIds();
        new ShellLauncher().OpenUnelevated(Cmd, "/d /c ping -n 6 127.0.0.1 >nul", null, showWindow: false);
        using var child = await WaitForNewPing(before);
        try { Assert.False(NativeProcess.IsElevated((uint)child.Id)); }
        finally { child.Kill(); }
    }

    [Fact]
    public async Task RunElevated_StartsAnElevatedChild()
    {
        if (!IsAdmin) return;
        var before = PingIds();
        new ShellLauncher().RunElevated(Cmd, "/d /c ping -n 6 127.0.0.1 >nul", showWindow: false);
        using var child = await WaitForNewPing(before);
        try { Assert.True(NativeProcess.IsElevated((uint)child.Id)); }
        finally { child.Kill(); }
    }
}
