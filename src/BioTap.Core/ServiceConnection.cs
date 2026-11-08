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
/// Events are raised on a background thread; the UI marshals them itself.
/// </summary>
public sealed class ServiceConnection(Func<IPipeClient> factory, TimeProvider time)
{
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    readonly Lock gate = new();
    IPipeClient? current;
    int? pendingTimeoutMs;
    ConnectionState? state;

    public event Action<ConnectionState>? StateChanged;
    public event Action<SensorState>? SensorStateChanged;
    public event Action<int>? Tapped;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SetState(ConnectionState.Connecting);
            var client = factory();
            try
            {
                using (var timeout = new CancellationTokenSource(ConnectTimeout, time))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
                    await client.ConnectAsync(linked.Token);

                int? pending;
                lock (gate) { current = client; pending = pendingTimeoutMs; pendingTimeoutMs = null; }
                SetState(ConnectionState.Connected);
                if (pending is { } ms) await client.SendAsync(new SetTimeoutMessage(ms), ct);

                await foreach (var m in client.ReadAllAsync(ct))
                {
                    if (m is TapsMessage t) Tapped?.Invoke(t.N);
                    else if (m is StatusMessage s) SensorStateChanged?.Invoke(s.State);
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
            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
            {
                SetState(ConnectionState.ServiceMissing);
            }
            finally
            {
                lock (gate) current = null;
                await client.DisposeAsync();
            }

            try { await Task.Delay(RetryDelay, time, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Sends now when connected; otherwise (or on failure) sends right after the next connection. Never throws.</summary>
    public async Task SendTimeoutAsync(int ms, CancellationToken ct)
    {
        IPipeClient? client;
        lock (gate)
        {
            client = current;
            if (client is null) { pendingTimeoutMs = ms; return; }
        }
        try
        {
            await client.SendAsync(new SetTimeoutMessage(ms), ct);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            lock (gate) pendingTimeoutMs = ms;
        }
    }

    void SetState(ConnectionState s)
    {
        lock (gate)
        {
            if (state == s) return;
            state = s;
        }
        StateChanged?.Invoke(s);
    }
}
