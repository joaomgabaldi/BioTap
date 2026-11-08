using System.Security.Principal;

namespace BioTap.Core.Tests;

[Trait("Category", "Elevated")]
public class StartupTaskElevatedTests
{
    static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [Fact]
    public void Enable_ThenDisable_RoundTrip()
    {
        if (!IsAdmin) return; // registering a highest-privileges task needs elevation
        var task = new StartupTask("BioTap-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.False(task.IsEnabled());
            task.Enable(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
            Assert.True(task.IsEnabled());
            task.Enable(Path.Combine(Environment.SystemDirectory, "notepad.exe")); // idempotent update
            task.Disable();
            Assert.False(task.IsEnabled());
            task.Disable(); // already gone: no error
        }
        finally { task.Disable(); }
    }
}
