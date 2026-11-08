using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using BioTap.Core;

namespace BioTap.Service;

/// <summary>
/// Serves the BioTap pipe: broadcasts messages to up to <see cref="MaxClients"/> clients and accepts only
/// <see cref="SetTimeoutMessage"/> from them. ACL: SYSTEM and Administrators only (spec §2, §7).
/// </summary>
public sealed class PipeServer : IAsyncDisposable
{
    public const string DefaultName = "BioTap";
    public const int MaxClients = 4;
    const int QueueLimit = 32;

    readonly string name;
    readonly ILogger log;
    readonly PipeSecurity? security;
    readonly ConcurrentDictionary<Client, byte> clients = new();
    readonly ConcurrentDictionary<Task, byte> handlers = new();

    public PipeServer(string pipeName, ILogger<PipeServer> log) : this(pipeName, log, CreateSecurity()) { }

    /// <summary>security null = default pipe ACL (tests only).</summary>
    internal PipeServer(string pipeName, ILogger log, PipeSecurity? security)
    {
        name = pipeName;
        this.log = log;
        this.security = security;
    }

    public event Action<SetTimeoutMessage>? TimeoutRequested;

    internal int ClientCount => clients.Count;

    public static PipeSecurity CreateSecurity()
    {
        var s = new PipeSecurity();
        s.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return s;
    }

    // MaxClients served + 1 listening + 1 just accepted and about to be rejected when full
    const int MaxInstances = MaxClients + 2;

    volatile byte[]? lastStatusLine;

    NamedPipeServerStream NewInstance(bool first)
    {
        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return security is null
            ? new NamedPipeServerStream(name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, options)
            : NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    /// <summary>
    /// Runs until cancelled. A listening instance always exists, so the pipe name is never unowned (a gap would let
    /// a local user create it with their own ACL). Throws if the name is taken at start or the server cannot keep it.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        NamedPipeServerStream listening;
        try
        {
            listening = NewInstance(first: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogError("Pipe {Name} already exists (possible pipe squatting): {Error}", name, e.GetType().Name);
            throw;
        }
        try
        {
            while (true)
            {
                try
                {
                    await listening.WaitForConnectionAsync(ct);
                }
                catch (IOException)
                {
                    // the client vanished mid-connect: reuse the same instance, so the name stays owned
                    try { listening.Disconnect(); } catch (Exception e) when (e is IOException or InvalidOperationException) { }
                    continue;
                }
                var connected = listening;
                try
                {
                    listening = NewInstance(first: false); // next instance exists before the accepted one is handed off
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log.LogError("Cannot keep a listening pipe instance ({Error}); stopping", e.GetType().Name);
                    connected.Dispose();
                    throw; // fail closed: the service stops instead of leaving the name to someone else
                }
                if (clients.Count >= MaxClients && clients.Keys.MinBy(c => c.Order) is { } oldest)
                {
                    // An idle or stale client must not lock out a new one (e.g. the app after a restart)
                    log.LogInformation("Pipe full; disconnecting the oldest client");
                    Drop(oldest);
                }
                var client = new Client(connected);
                if (lastStatusLine is { } status) client.Queue.Writer.TryWrite(status);
                clients[client] = 0;
                var handler = ServeAsync(client, ct);
                handlers[handler] = 0;
                _ = handler.ContinueWith(t => handlers.TryRemove(t, out _), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            listening.Dispose();
            foreach (var c in clients.Keys) Drop(c);
        }
    }

    /// <summary>Never blocks: a client whose queue is full is disconnected. The last status is replayed to new clients.</summary>
    public void Broadcast(Message m)
    {
        var line = Encoding.UTF8.GetBytes(Protocol.Serialize(m) + "\n");
        if (m is StatusMessage) lastStatusLine = line;
        foreach (var c in clients.Keys)
            if (!c.Queue.Writer.TryWrite(line) && clients.ContainsKey(c)) // a client dropped meanwhile is not "not reading"
            {
                log.LogWarning("Pipe client not reading; disconnecting it");
                Drop(c);
            }
    }

    async Task ServeAsync(Client c, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writer = WriteLoopAsync(c, linked.Token);
        try
        {
            var reader = new LineReader(c.Pipe);
            while (true)
            {
                var (status, line) = await reader.ReadLineAsync(linked.Token);
                if (status == LineStatus.TooLong) log.LogWarning("Pipe client sent an oversized line; disconnecting it");
                if (status != LineStatus.Line) break;
                if (Protocol.TryParse(line!, out var m) && m is SetTimeoutMessage t) RaiseTimeoutRequested(t);
                else log.LogDebug("Ignored pipe message");
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e)
        {
            log.LogError("Pipe client handler failed: {Error}", e.GetType().Name);
        }
        finally
        {
            await linked.CancelAsync();
            Drop(c);
            await writer;
        }
    }

    void RaiseTimeoutRequested(SetTimeoutMessage t)
    {
        try
        {
            TimeoutRequested?.Invoke(t);
        }
        catch (Exception e)
        {
            log.LogError("setTimeout handler failed: {Error}", e.GetType().Name);
        }
    }

    async Task WriteLoopAsync(Client c, CancellationToken ct)
    {
        try
        {
            await foreach (var line in c.Queue.Reader.ReadAllAsync(ct))
                await c.Pipe.WriteAsync(line, ct);
        }
        catch (Exception e)
        {
            if (e is not (IOException or OperationCanceledException or ObjectDisposedException))
                log.LogError("Pipe writer failed: {Error}", e.GetType().Name);
            Drop(c);
        }
    }

    void Drop(Client c)
    {
        if (!clients.TryRemove(c, out _)) return;
        c.Queue.Writer.TryComplete();
        c.Pipe.Dispose();
    }

    internal int RunningHandlers => handlers.Keys.Count(t => !t.IsCompleted);

    public async ValueTask DisposeAsync()
    {
        foreach (var c in clients.Keys) Drop(c);
        await Task.WhenAll(handlers.Keys);
    }

    sealed class Client(NamedPipeServerStream pipe)
    {
        static long nextOrder;
        public long Order { get; } = Interlocked.Increment(ref nextOrder);
        public NamedPipeServerStream Pipe { get; } = pipe;
        public Channel<byte[]> Queue { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueLimit) { SingleReader = true });
    }
}
