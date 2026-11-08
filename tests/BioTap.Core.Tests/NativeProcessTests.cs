using System.Diagnostics;

namespace BioTap.Core.Tests;

public class NativeProcessTests
{
    [Fact]
    public void Find_ReturnsThisProcessWithSessionAndPath()
    {
        using var me = Process.GetCurrentProcess();
        var found = NativeProcess.Find([me.ProcessName]);
        var self = Assert.Single(found, p => p.ImagePath is not null && string.Equals(p.ImagePath, me.MainModule!.FileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(me.SessionId, self.SessionId);
    }

    [Fact]
    public void Find_UnknownName_ReturnsNothing()
        => Assert.Empty(NativeProcess.Find(["biotap-no-such-process-" + Guid.NewGuid()]));
}
