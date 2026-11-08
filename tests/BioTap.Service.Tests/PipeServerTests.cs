using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BioTap.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTap.Service.Tests;

public sealed class PipeServerTests : IAsyncDisposable
{
    readonly string name = "BioTap-test-" + Guid.NewGuid();
    readonly CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
    PipeServer? server;
    Task? running;

    PipeServer Start()
    {
        server = new PipeServer(name, NullLogger.Instance, security: null); // default ACL so the test user can connect
        running = server.RunAsync(cts.Token);
        return server;
    }

    async Task<NamedPipeClientStream> ConnectRaw()
    {
        var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c.ConnectAsync(cts.Token);
        return c;
    }

    async Task WaitUntil(Func<bool> condition)
    {
        while (!condition()) await Task.Delay(20, cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        if (running is not null) await running;
        if (server is not null) await server.DisposeAsync();
        cts.Dispose();
    }

    [Fact]
    public void CreateSecurity_OnlySystemAndAdmins()
    {
        var rules = PipeServer.CreateSecurity().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Assert.Equal(["S-1-5-18", "S-1-5-32-544"], rules.Select(r => r.IdentityReference.Value).Order());
        Assert.All(rules, r =>
        {
            Assert.Equal(AccessControlType.Allow, r.AccessControlType);
            Assert.Equal(PipeAccessRights.FullControl, r.PipeAccessRights & PipeAccessRights.FullControl);
        });
    }

    [Fact]
    public async Task Server_FailsIfNameAlreadyTaken()
    {
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var s = new PipeServer(name, NullLogger.Instance, security: null);
        await Assert.ThrowsAnyAsync<Exception>(() => s.RunAsync(cts.Token));
        Assert.Equal(0, s.ClientCount);
    }

    [Fact]
    public async Task Broadcast_ReachesConnectedClient()
    {
        var s = Start();
        await using var c = await ConnectRaw();
        await WaitUntil(() => s.ClientCount == 1);
        s.Broadcast(new TapsMessage(2));
        var (status, line) = await new LineReader(c).ReadLineAsync(cts.Token);
        Assert.Equal((LineStatus.Line, """{"type":"taps","n":2}"""), (status, line));
    }

    [Fact]
    public async Task Broadcast_DeadClient_IsDroppedOthersStillReceive()
    {
        var s = Start();
        await using var dead = await ConnectRaw(); // never reads
        await using var live = await ConnectRaw();
        await WaitUntil(() => s.ClientCount == 2);

        // Lockstep: the live client reads every message before the next one is sent, so only the dead one can fall behind.
        var reader = new LineReader(live);
        for (var i = 0; s.ClientCount == 2; i++)
        {
            Assert.True(i < 100_000, "dead client was never dropped");
            s.Broadcast(new TapsMessage(1));
            Assert.Equal(LineStatus.Line, (await reader.ReadLineAsync(cts.Token)).Status);
        }
        Assert.Equal(1, s.ClientCount);
        s.Broadcast(new TapsMessage(2));
        Assert.Equal("""{"type":"taps","n":2}""", (await reader.ReadLineAsync(cts.Token)).Line);
    }

    [Fact]
    public async Task Server_OversizedLine_DisconnectsClient()
    {
        var s = Start();
        await using var c = await ConnectRaw();
        await WaitUntil(() => s.ClientCount == 1);
        try { await c.WriteAsync(Encoding.UTF8.GetBytes(new string('a', 5000)), cts.Token); }
        catch (IOException) { } // server may drop us before the write finishes: that is the expected outcome
        await WaitUntil(() => s.ClientCount == 0);
    }

    [Fact]
    public async Task Server_SetTimeout_RaisesEvent()
    {
        var s = Start();
        var got = new TaskCompletionSource<SetTimeoutMessage>();
        s.TimeoutRequested += m => got.TrySetResult(m);
        await using var c = await ConnectRaw();
        await c.WriteAsync(Encoding.UTF8.GetBytes("""{"type":"setTimeout","ms":1200}""" + "\n"), cts.Token);
        Assert.Equal(new SetTimeoutMessage(1200), await got.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task Server_ThrowingSubscriber_DoesNotDropClient()
    {
        var s = Start();
        var calls = 0;
        s.TimeoutRequested += _ => { Interlocked.Increment(ref calls); throw new InvalidOperationException("boom"); };
        await using var c = await ConnectRaw();
        var line = Encoding.UTF8.GetBytes("""{"type":"setTimeout","ms":1200}""" + "\n");
        await c.WriteAsync(line, cts.Token);
        await WaitUntil(() => Volatile.Read(ref calls) == 1);
        await c.WriteAsync(line, cts.Token);
        await WaitUntil(() => Volatile.Read(ref calls) == 2);
        Assert.Equal(1, s.ClientCount);
    }

    [Fact]
    public async Task Server_KeepsAcceptingAfterAllClientsDropAtOnce()
    {
        var s = Start();
        for (var round = 0; round < 5; round++)
        {
            var batch = new List<NamedPipeClientStream>();
            for (var i = 0; i < PipeServer.MaxClients; i++) batch.Add(await ConnectRaw());
            await WaitUntil(() => s.ClientCount == PipeServer.MaxClients);
            foreach (var c in batch) await c.DisposeAsync();
            await WaitUntil(() => s.ClientCount == 0);
        }
        await using var last = await ConnectRaw();
        await WaitUntil(() => s.ClientCount == 1);
        Assert.False(running!.IsCompleted);
    }

    [Fact]
    public async Task Server_NeverLeavesTheNameUnowned()
    {
        var s = Start();
        var batch = new List<NamedPipeClientStream>();
        for (var i = 0; i < PipeServer.MaxClients; i++) batch.Add(await ConnectRaw());
        await WaitUntil(() => s.ClientCount == PipeServer.MaxClients);
        foreach (var c in batch) await c.DisposeAsync();
        await WaitUntil(() => s.ClientCount == 0);
        // A squatter creating the name now must fail: the server still owns a listening instance.
        Assert.ThrowsAny<Exception>(() => new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance));
    }

    [Fact]
    public async Task NewClient_ReceivesLastStatusOnConnect()
    {
        var s = Start();
        s.Broadcast(new StatusMessage(SensorState.Ok)); // sent before anyone is connected
        s.Broadcast(new TapsMessage(3));                // taps are not replayed
        await using var c = await ConnectRaw();
        var (status, line) = await new LineReader(c).ReadLineAsync(cts.Token);
        Assert.Equal((LineStatus.Line, """{"type":"status","state":"ok"}"""), (status, line));
    }

    [Fact]
    public async Task Server_InvalidOrUnexpectedMessage_IsIgnoredAndClientKept()
    {
        var s = Start();
        var raised = false;
        s.TimeoutRequested += _ => raised = true;
        await using var c = await ConnectRaw();
        await WaitUntil(() => s.ClientCount == 1);
        await c.WriteAsync(Encoding.UTF8.GetBytes("garbage\n" + """{"type":"taps","n":5}""" + "\n" + """{"type":"setTimeout","ms":10}""" + "\n"), cts.Token);
        await Task.Delay(200, cts.Token);
        Assert.False(raised);
        Assert.Equal(1, s.ClientCount);
    }
}
