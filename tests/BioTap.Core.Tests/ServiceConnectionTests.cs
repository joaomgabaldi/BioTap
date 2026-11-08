using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

/// <summary>Scripted pipe client: the test decides how ConnectAsync behaves and pushes messages.</summary>
sealed class FakePipeClient(Exception? connectError = null) : IPipeClient
{
    public readonly Channel<Message?> Incoming = Channel.CreateUnbounded<Message?>(); // null = server closed
    public readonly ConcurrentQueue<Message> Sent = new();

    public Task ConnectAsync(CancellationToken ct) => connectError is null ? Task.CompletedTask : Task.FromException(connectError);

    public async IAsyncEnumerable<Message> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in Incoming.Reader.ReadAllAsync(ct))
        {
            if (m is null) yield break;
            yield return m;
        }
    }

    public Task SendAsync(Message m, CancellationToken ct) { Sent.Enqueue(m); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ServiceConnectionTests : IAsyncDisposable
{
    readonly FakeTimeProvider time = new();
    readonly ConcurrentQueue<FakePipeClient> clients = new();
    readonly Queue<Func<FakePipeClient>> script = new();
    readonly ConcurrentQueue<ConnectionState> states = new();
    readonly ConcurrentQueue<SensorState> sensor = new();
    readonly ConcurrentQueue<int> taps = new();
    readonly CancellationTokenSource cts = new();
    ServiceConnection? conn;
    Task? running;

    IPipeClient Next()
    {
        var c = script.Count > 0 ? script.Dequeue()() : new FakePipeClient();
        clients.Enqueue(c);
        return c;
    }

    ServiceConnection Start()
    {
        conn = new ServiceConnection(Next, time);
        conn.StateChanged += states.Enqueue;
        conn.SensorStateChanged += sensor.Enqueue;
        conn.Tapped += taps.Enqueue;
        running = conn.RunAsync(cts.Token);
        return conn;
    }

    static async Task WaitUntil(Func<bool> c)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!c()) { if (Environment.TickCount64 > deadline) throw new TimeoutException(); await Task.Delay(10); }
    }

    async Task Tick(int ms) { time.Advance(TimeSpan.FromMilliseconds(ms)); await Task.Delay(20); }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        if (running is not null) await running;
    }

    [Fact]
    public async Task Connection_ReceivesStatusThenTaps()
    {
        Start();
        await WaitUntil(() => clients.Count == 1);
        var c = clients.Single();
        await c.Incoming.Writer.WriteAsync(new StatusMessage(SensorState.Ok));
        await c.Incoming.Writer.WriteAsync(new TapsMessage(3));
        await WaitUntil(() => taps.Contains(3));
        Assert.Contains(ConnectionState.Connected, states);
        Assert.Equal([SensorState.Ok], sensor);
    }

    [Fact]
    public async Task Connection_ServiceRestart_Reconnects()
    {
        Start();
        await WaitUntil(() => clients.Count == 1);
        await clients.Single().Incoming.Writer.WriteAsync(null); // server closed
        await WaitUntil(() => states.LastOrDefault() == ConnectionState.Connecting);
        await Tick(1999);
        Assert.Single(clients);
        await Tick(1);
        await WaitUntil(() => clients.Count == 2);
        await WaitUntil(() => states.LastOrDefault() == ConnectionState.Connected);
    }

    [Fact]
    public async Task Connection_VerificationFails_NotTrustedAndRetries()
    {
        script.Enqueue(() => new FakePipeClient(new ServerVerificationException("fake server")));
        Start();
        await WaitUntil(() => states.Contains(ConnectionState.NotTrusted));
        await Tick(2000);
        await WaitUntil(() => states.LastOrDefault() == ConnectionState.Connected);
    }

    [Fact]
    public async Task Connection_NotInstalled_ServiceMissing()
    {
        script.Enqueue(() => new FakePipeClient(new TimeoutException()));
        script.Enqueue(() => new FakePipeClient(new IOException()));
        Start();
        await WaitUntil(() => states.Contains(ConnectionState.ServiceMissing));
    }

    [Fact]
    public async Task SendTimeout_WhenDisconnected_DoesNotThrow_AndIsSentOnConnect()
    {
        script.Enqueue(() => new FakePipeClient(new IOException()));
        var c = Start();
        await WaitUntil(() => states.Contains(ConnectionState.ServiceMissing));
        await c.SendTimeoutAsync(1200, CancellationToken.None);
        await Tick(2000);
        await WaitUntil(() => clients.Count == 2 && clients.Last().Sent.Contains(new SetTimeoutMessage(1200)));
    }

    [Fact]
    public async Task SendTimeout_WhenConnected_SendsImmediately()
    {
        var c = Start();
        await WaitUntil(() => states.LastOrDefault() == ConnectionState.Connected);
        await c.SendTimeoutAsync(900, CancellationToken.None);
        Assert.Contains(new SetTimeoutMessage(900), clients.Single().Sent);
    }
}
