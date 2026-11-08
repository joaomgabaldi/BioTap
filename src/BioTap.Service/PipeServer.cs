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

    NamedPipeServerStream NewInstance(bool first)
    {
        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return security is null
            ? new NamedPipeServerStream(name, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte, options)
            : NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    /// <summary>Runs until cancelled. Throws if another process already owns the pipe name (pipe squatting).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        NamedPipeServerStream next;
        try
        {
            next = NewInstance(first: true);
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
                await next.WaitForConnectionAsync(ct);
                var client = new Client(next);
                clients[client] = 0;
                _ = ServeAsync(client, ct);
                while (clients.Count >= MaxClients) await Task.Delay(200, ct);
                next = NewInstance(first: false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            next.Dispose();
            foreach (var c in clients.Keys) Drop(c);
        }
    }

    /// <summary>Never blocks: a client whose queue is full is disconnected.</summary>
    public void Broadcast(Message m)
    {
        var line = Encoding.UTF8.GetBytes(Protocol.Serialize(m) + "\n");
        foreach (var c in clients.Keys)
            if (!c.Queue.Writer.TryWrite(line))
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
                if (Protocol.TryParse(line!, out var m) && m is SetTimeoutMessage t) TimeoutRequested?.Invoke(t);
                else log.LogDebug("Ignored pipe message");
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            await linked.CancelAsync();
            Drop(c);
            await writer;
        }
    }

    async Task WriteLoopAsync(Client c, CancellationToken ct)
    {
        try
        {
            await foreach (var line in c.Queue.Reader.ReadAllAsync(ct))
                await c.Pipe.WriteAsync(line, ct);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Drop(c);
        }
    }

    void Drop(Client c)
    {
        if (!clients.TryRemove(c, out _)) return;
        c.Queue.Writer.TryComplete();
        c.Pipe.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var c in clients.Keys) Drop(c);
        return ValueTask.CompletedTask;
    }

    sealed class Client(NamedPipeServerStream pipe)
    {
        public NamedPipeServerStream Pipe { get; } = pipe;
        public Channel<byte[]> Queue { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueLimit) { SingleReader = true });
    }
}
