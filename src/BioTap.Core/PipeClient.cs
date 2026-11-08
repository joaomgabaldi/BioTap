using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Services;
using Windows.Win32.System.Threading;

namespace BioTap.Core;

public sealed class ServerVerificationException(string message) : Exception(message);

/// <summary>
/// Client side of the service pipe. Before trusting anything, it checks that the process serving the pipe is the
/// running BioTap.Service (PID from the SCM) and its image is the installed exe (spec §2, §7).
/// </summary>
public sealed class PipeClient(string pipeName, string expectedServerExe) : IAsyncDisposable
{
    public const string ServiceName = "BioTap.Service";

    NamedPipeClientStream? pipe;
    LineReader? reader;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await p.ConnectAsync(ct);
            if (!PInvoke.GetNamedPipeServerProcessId(p.SafePipeHandle, out var pid))
                throw new ServerVerificationException("Could not read the pipe server PID");
            if (!VerifyServer((int)pid, QueryServicePid(ServiceName), QueryImagePath(pid), expectedServerExe, out var reason))
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
        var r = reader ?? throw new InvalidOperationException("Not connected");
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
        var p = pipe ?? throw new InvalidOperationException("Not connected");
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

    static string? QueryImagePath(uint pid)
    {
        using var proc = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proc.IsInvalid) return null;
        Span<char> path = stackalloc char[1024];
        var size = (uint)path.Length;
        return PInvoke.QueryFullProcessImageName(proc, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref size)
            ? new string(path[..(int)size])
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (pipe is not null) await pipe.DisposeAsync();
        pipe = null;
        reader = null;
    }
}
