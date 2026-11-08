using System.IO.Pipes;

namespace BioTap.Core.Tests;

public class PipeClientTests
{
    const string Exe = @"C:\Program Files\BioTap\BioTap.Service.exe";

    [Fact]
    public void Verify_AllMatch_Accepts()
        => Assert.True(PipeClient.VerifyServer(42, 42, Exe, Exe, out _));

    [Fact]
    public void Verify_PidDiffersFromService_Rejects()
    {
        Assert.False(PipeClient.VerifyServer(42, 43, Exe, Exe, out var reason));
        Assert.Contains("PID", reason);
    }

    [Fact]
    public void Verify_ServiceNotRunning_Rejects()
        => Assert.False(PipeClient.VerifyServer(42, null, Exe, Exe, out _));

    [Fact]
    public void Verify_WrongImagePath_Rejects()
        => Assert.False(PipeClient.VerifyServer(42, 42, @"C:\Users\x\AppData\Local\Temp\BioTap.Service.exe", Exe, out _));

    [Fact]
    public void Verify_UnknownImage_Rejects()
        => Assert.False(PipeClient.VerifyServer(42, 42, null, Exe, out _));

    [Fact]
    public void Verify_PathCaseAndSlashDifferences_Accepts()
        => Assert.True(PipeClient.VerifyServer(42, 42, "c:/program files/biotap/BIOTAP.SERVICE.EXE", Exe, out _));

    [Fact]
    public void Verify_PathTraversalTrick_Rejects()
        => Assert.False(PipeClient.VerifyServer(42, 42, @"C:\Program Files\BioTap\..\Evil\BioTap.Service.exe", Exe, out _));

    [Fact]
    public async Task Client_RejectsServerThatIsNotTheService()
    {
        var name = "BioTap-test-" + Guid.NewGuid();
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();
        await using var client = new PipeClient(name, Exe);
        await Assert.ThrowsAsync<ServerVerificationException>(() => client.ConnectAsync(CancellationToken.None));
        await accept;
    }
}
