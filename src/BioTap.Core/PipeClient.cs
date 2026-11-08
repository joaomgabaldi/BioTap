using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Services;

namespace BioTap.Core;

public sealed class ServerVerificationException(string message) : Exception(message);

/// <summary>
/// Client side of the service pipe. Before trusting anything, it checks that the process serving the pipe is the
/// running BioTap.Service (PID from the SCM) and its image is the installed exe (spec §2, §7).
/// </summary>
public sealed class PipeClient(string pipeName, string expectedServerExe) : IPipeClient
{
    public const string ServiceName = "BioTap.Service";

    NamedPipeClientStream? pipe;
    LineReader? reader;

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Identification: a server must never be able to impersonate this admin client
        var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await p.ConnectAsync(ct);
            if (!PInvoke.GetNamedPipeServerProcessId(p.SafePipeHandle, out var pid))
                throw new ServerVerificationException("Could not read the pipe server PID");
            // Hold the process handle for the whole check so the PID cannot be reused in between
            using var server = NativeProcess.Open(pid);
            if (!VerifyServer((int)pid, QueryServicePid(ServiceName), NativeProcess.ImagePath(server), expectedServerExe, out var reason))
                throw new ServerVerificationException(reason);
        }
        catch
        {
            await p.DisposeAsync();
            throw;
        }
        pipe = p;
        reader = new LineReader(p);
    }

    /// <summary>Yields valid messages; skips invalid lines; ends (and disconnects) on EOF or an oversized line.</summary>
    public async IAsyncEnumerable<Message> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var r = reader ?? throw new IOException("Not connected");
        while (true)
        {
            var (status, line) = await r.ReadLineAsync(ct);
            if (status != LineStatus.Line)
            {
                await DisposeAsync();
                yield break;
            }
            if (Protocol.TryParse(line!, out var m)) yield return m!;
        }
    }

    public async Task SendAsync(Message m, CancellationToken ct)
    {
        var p = pipe ?? throw new IOException("Not connected"); // same exception type as a broken pipe
        await p.WriteAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(m) + "\n"), ct);
        await p.FlushAsync(ct);
    }

    internal static bool VerifyServer(int serverPid, int? servicePid, string? serverImage, string expectedExe, out string reason)
    {
        if (servicePid is null)
        {
            reason = $"{ServiceName} is not running";
            return false;
        }
        if (serverPid != servicePid)
        {
            reason = $"Pipe server PID {serverPid} is not the {ServiceName} PID {servicePid}";
            return false;
        }
        expectedExe = Environment.ExpandEnvironmentVariables(expectedExe);
        if (serverImage is null || !string.Equals(Path.GetFullPath(serverImage), Path.GetFullPath(expectedExe), StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Pipe server image '{serverImage}' is not '{expectedExe}'";
            return false;
        }
        reason = "";
        return true;
    }

    static int? QueryServicePid(string serviceName)
    {
        using var scm = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        if (scm.IsInvalid) return null;
        using var svc = PInvoke.OpenService(scm, serviceName, PInvoke.SERVICE_QUERY_STATUS);
        if (svc.IsInvalid) return null;
        Span<byte> buf = stackalloc byte[Marshal.SizeOf<SERVICE_STATUS_PROCESS>()];
        if (!PInvoke.QueryServiceStatusEx(svc, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, buf, out _)) return null;
        var pid = MemoryMarshal.Read<SERVICE_STATUS_PROCESS>(buf).dwProcessId;
        return pid == 0 ? null : (int)pid;
    }

    public async ValueTask DisposeAsync()
    {
        if (pipe is not null) await pipe.DisposeAsync();
        pipe = null;
        reader = null;
    }
}
