using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

/// <summary>Pipe client whose connect never completes and ignores cancellation (stuck SCM / process query).</summary>
sealed class HangingPipeClient : IPipeClient
{
    public Task ConnectAsync(CancellationToken ct) => new TaskCompletionSource().Task;
    public async IAsyncEnumerable<Message> ReadAllAsync([EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; }
    public Task SendAsync(Message m, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Connect never completes on its own; records the token so a test can see whether the attempt was stopped.</summary>
sealed class TokenRecordingHangingClient : IPipeClient
{
    public CancellationToken Token;
    public Task ConnectAsync(CancellationToken ct) { Token = ct; return new TaskCompletionSource().Task; }
    public async IAsyncEnumerable<Message> ReadAllAsync([EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; }
    public Task SendAsync(Message m, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FailingSendClient : IPipeClient
{
    public readonly Channel<Message?> Incoming = Channel.CreateUnbounded<Message?>();
    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public async IAsyncEnumerable<Message> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in Incoming.Reader.ReadAllAsync(ct)) { if (m is null) yield break; yield return m; }
    }
    public Task SendAsync(Message m, CancellationToken ct) => Task.FromException(new IOException("pipe broke"));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ServiceConnectionReviewTests : IAsyncDisposable
{
    readonly FakeTimeProvider time = new();
    readonly Queue<Func<IPipeClient>> script = new();
    readonly ConcurrentQueue<IPipeClient> made = new();
    readonly ConcurrentQueue<ConnectionState> states = new();
    readonly CancellationTokenSource cts = new();
    Task? running;

    ServiceConnection Start(Action<ServiceConnection>? setup = null)
    {
        var c = new ServiceConnection(() =>
        {
            var next = script.Count > 0 ? script.Dequeue()() : new FakePipeClient();
            made.Enqueue(next);
            return next;
        }, time);
        c.StateChanged += states.Enqueue;
        setup?.Invoke(c);
        running = c.RunAsync(cts.Token);
        return c;
    }

    /// <summary>Advances fake time in steps until the condition holds (timers are registered asynchronously).</summary>
    async Task TickUntil(Func<bool> condition, int maxSteps = 200)
    {
        for (var i = 0; i < maxSteps && !condition(); i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10);
        }
        Assert.True(condition(), "states: " + string.Join(",", states));
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(Win32Exception))]
    public async Task UnexpectedConnectError_KeepsRetrying(Type error)
    {
        script.Enqueue(() => new FakePipeClient((Exception)Activator.CreateInstance(error)!));
        Start();
        await TickUntil(() => states.LastOrDefault() == ConnectionState.Connected);
        Assert.Equal(2, made.Count);
    }

    [Fact]
    public async Task FactoryThrows_KeepsRetrying()
    {
        script.Enqueue(() => throw new InvalidOperationException("factory"));
        Start();
        await TickUntil(() => states.LastOrDefault() == ConnectionState.Connected);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotKillTheLoop()
    {
        Start(c => c.StateChanged += _ => throw new InvalidOperationException("ui bug"));
        await TickUntil(() => states.LastOrDefault() == ConnectionState.Connected);
        var first = (FakePipeClient)made.Single();
        await first.Incoming.Writer.WriteAsync(null);
        await TickUntil(() => made.Count == 2 && states.LastOrDefault() == ConnectionState.Connected);
    }

    [Fact]
    public async Task HangingConnect_TimesOutAndReportsServiceMissing()
    {
        script.Enqueue(() => new HangingPipeClient());
        Start();
        await TickUntil(() => states.Contains(ConnectionState.ServiceMissing));
    }

    [Fact]
    public async Task TimedOutConnect_IsCancelled_SoNoAttemptKeepsRunning()
    {
        // A connect left running after the timeout spins a pool thread until the pipe appears; they pile up
        var hanging = new TokenRecordingHangingClient();
        script.Enqueue(() => hanging);
        Start();
        await TickUntil(() => states.Contains(ConnectionState.ServiceMissing));
        Assert.True(hanging.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task FailedPendingSend_IsRetriedOnNextConnection()
    {
        script.Enqueue(() => new FakePipeClient(new IOException()));
        script.Enqueue(() => new FailingSendClient());
        var c = Start();
        await TickUntil(() => states.Contains(ConnectionState.ServiceMissing));
        await c.SendTimeoutAsync(1500, CancellationToken.None);
        await TickUntil(() => made.Count >= 2 && states.LastOrDefault() == ConnectionState.Connected);
        await ((FailingSendClient)made.ElementAt(1)).Incoming.Writer.WriteAsync(null); // drop it
        await TickUntil(() => made.Count >= 3 && ((FakePipeClient)made.ElementAt(2)).Sent.Contains(new SetTimeoutMessage(1500)));
    }

    [Fact]
    public async Task Outage_DoesNotFlapState()
    {
        for (var i = 0; i < 5; i++) script.Enqueue(() => new FakePipeClient(new IOException()));
        Start();
        await TickUntil(() => made.Count >= 5);
        Assert.Equal([ConnectionState.Connecting, ConnectionState.ServiceMissing], states.Distinct());
        Assert.Equal(2, states.Count);
    }

    [Fact]
    public async Task Cancel_ExitsCleanly()
    {
        Start();
        await TickUntil(() => states.LastOrDefault() == ConnectionState.Connected);
        cts.Cancel();
        await running!.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
