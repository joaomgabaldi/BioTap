namespace BioTap.Core;

public interface IPipeClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    IAsyncEnumerable<Message> ReadAllAsync(CancellationToken ct);
    Task SendAsync(Message m, CancellationToken ct);
}

public enum ConnectionState { Connecting, Connected, ServiceMissing, NotTrusted }

/// <summary>
/// Keeps the app connected to BioTap.Service: reconnects every 2 s, reports state, sensor status and taps.
/// The loop never ends on its own: any failure means "try again". Events are raised on a background thread,
/// each handler isolated (a UI bug cannot break the connection).
/// </summary>
public sealed class ServiceConnection(Func<IPipeClient> factory, TimeProvider time)
{
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    readonly Lock gate = new();
    readonly SemaphoreSlim sendLock = new(1, 1); // one writer at a time on the pipe
    IPipeClient? current;
    int? pendingTimeoutMs; // latest wanted value not yet confirmed sent
    ConnectionState? state;

    public event Action<ConnectionState>? StateChanged;
    public event Action<SensorState>? SensorStateChanged;
    public event Action<int>? Tapped;

    /// <summary>Runs until <paramref name="ct"/> is cancelled, on a pool thread (never on the caller's).</summary>
    public Task RunAsync(CancellationToken ct) => Task.Run(() => LoopAsync(ct), CancellationToken.None);

    async Task LoopAsync(CancellationToken ct)
    {
        SetState(ConnectionState.Connecting);
        while (!ct.IsCancellationRequested)
        {
            IPipeClient? client = null;
            var connected = false;
            try
            {
                client = factory();
                // WaitAsync: the client's own connect may do uncancellable work (SCM, process queries)
                await client.ConnectAsync(ct).WaitAsync(ConnectTimeout, time, ct);
                connected = true;
                await FlushPendingAsync(client, ct);
                lock (gate) current = client;
                SetState(ConnectionState.Connected);

                await foreach (var m in client.ReadAllAsync(ct))
                {
                    if (m is TapsMessage t) Raise(Tapped, t.N);
                    else if (m is StatusMessage s) Raise(SensorStateChanged, s.State);
                }
                SetState(ConnectionState.Connecting); // the service closed the pipe (restart, stop)
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ServerVerificationException)
            {
                SetState(ConnectionState.NotTrusted);
            }
            catch (Exception)
            {
                // Whatever went wrong, the answer is to retry; only the state shown differs
                SetState(connected ? ConnectionState.Connecting : ConnectionState.ServiceMissing);
            }
            finally
            {
                lock (gate) current = null;
                if (client is not null)
                {
                    try { await client.DisposeAsync(); } catch (Exception) { }
                }
            }

            try { await Task.Delay(RetryDelay, time, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Sends now when connected, otherwise right after the next connection; the latest value wins. Never throws.</summary>
    public async Task SendTimeoutAsync(int ms, CancellationToken ct)
    {
        IPipeClient? client;
        lock (gate)
        {
            pendingTimeoutMs = ms;
            client = current;
        }
        if (client is null) return;
        try
        {
            await FlushPendingAsync(client, ct);
        }
        catch (Exception)
        {
            // stays pending; the next connection sends it
        }
    }

    /// <summary>Sends the pending timeout; clears it only after a successful send of that exact value.</summary>
    async Task FlushPendingAsync(IPipeClient client, CancellationToken ct)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            int? value;
            lock (gate) value = pendingTimeoutMs;
            if (value is not { } ms) return;
            await client.SendAsync(new SetTimeoutMessage(ms), ct);
            lock (gate)
                if (pendingTimeoutMs == ms) pendingTimeoutMs = null;
        }
        finally
        {
            sendLock.Release();
        }
    }

    void SetState(ConnectionState s)
    {
        lock (gate)
        {
            if (state == s) return;
            state = s;
        }
        Raise(StateChanged, s);
    }

    static void Raise<T>(Action<T>? handlers, T value)
    {
        foreach (var h in handlers?.GetInvocationList() ?? [])
        {
            try { ((Action<T>)h)(value); }
            catch (Exception) { }
        }
    }
}
