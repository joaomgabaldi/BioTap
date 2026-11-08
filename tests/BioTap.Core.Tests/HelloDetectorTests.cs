namespace BioTap.Core.Tests;

public class HelloDetectorTests
{
    const string Win = @"C:\Windows";
    const int Console = 1;

    static bool Detect(params ProcessInfo[] ps) => HelloDetector.IsHelloActive(ps, Console, Win);

    [Fact]
    public void HelloProcessInConsoleSession_FromWindows_IsHello()
        => Assert.True(Detect(new ProcessInfo("consent", Console, @"C:\Windows\System32\consent.exe")));

    [Fact]
    public void EnrollmentHostUnderSystemApps_IsHello()
        => Assert.True(Detect(new ProcessInfo("BioEnrollmentHost", Console, @"C:\Windows\SystemApps\Microsoft.BioEnrollment_cw5n1h2txyewy\BioEnrollmentHost.exe")));

    [Fact]
    public void SameNameInAnotherSession_IsIgnored()
        => Assert.False(Detect(new ProcessInfo("LogonUI", 2, @"C:\Windows\System32\LogonUI.exe")));

    [Fact]
    public void SameNameOutsideWindows_IsIgnored()
        => Assert.False(Detect(new ProcessInfo("consent", Console, @"C:\Users\x\AppData\Local\Temp\consent.exe")));

    [Fact]
    public void LookalikeWindowsPrefix_IsIgnored()
        => Assert.False(Detect(new ProcessInfo("consent", Console, @"C:\WindowsEvil\consent.exe")));

    [Fact]
    public void UnknownImage_CountsAsHello() // fail safe: when in doubt, leave the sensor to Hello
        => Assert.True(Detect(new ProcessInfo("CredentialUIBroker", Console, null)));

    [Fact]
    public void NoConsoleSession_AnySessionCounts()
        => Assert.True(HelloDetector.IsHelloActive([new("LogonUI", 5, @"C:\Windows\System32\LogonUI.exe")], -1, Win));

    [Fact]
    public void NamesAreCaseInsensitive()
        => Assert.True(Detect(new ProcessInfo("logonui", Console, @"c:\windows\system32\logonui.exe")));

    [Fact]
    public void OtherProcesses_AreIgnored()
        => Assert.False(Detect(new ProcessInfo("notepad", Console, @"C:\Windows\System32\notepad.exe"), new ProcessInfo("explorer", Console, @"C:\Windows\explorer.exe")));

    [Fact]
    public void NoProcesses_IsNotHello() => Assert.False(Detect());
}

public class HelloDetectorSuspendedTests
{
    [Fact]
    public void SuspendedHelloProcess_IsIgnored()
        // BioEnrollmentHost stays alive, suspended, after Settings closes the enrollment page
        => Assert.False(HelloDetector.IsHelloActive(
            [new ProcessInfo("BioEnrollmentHost", 1, @"C:\Windows\SystemApps\Microsoft.BioEnrollment_cw5n1h2txyewy\BioEnrollmentHost.exe", Suspended: true)],
            1, @"C:\Windows"));

    [Fact]
    public void RunningHelloProcess_StillCounts()
        => Assert.True(HelloDetector.IsHelloActive(
            [new ProcessInfo("BioEnrollmentHost", 1, @"C:\Windows\SystemApps\Microsoft.BioEnrollment_cw5n1h2txyewy\BioEnrollmentHost.exe", Suspended: false)],
            1, @"C:\Windows"));
}
