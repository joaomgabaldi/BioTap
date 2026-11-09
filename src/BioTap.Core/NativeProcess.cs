using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using System.Diagnostics;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace BioTap.Core;

public static class NativeProcess
{
    /// <summary>Opens a query handle; while it is held, the PID cannot be reused by another process.</summary>
    public static SafeFileHandle Open(uint pid)
        => PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

    public static string? ImagePath(SafeFileHandle process)
    {
        if (process.IsInvalid) return null;
        Span<char> path = stackalloc char[1024];
        var size = (uint)path.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref size)
            ? new string(path[..(int)size])
            : null;
    }

    /// <summary>
    /// Processes whose name (without ".exe") is in <paramref name="names"/>, with session and image path, from one
    /// snapshot. Measured on this PC: ~2.8 ms per call (a Toolhelp snapshot was slower, ~5.4 ms).
    /// </summary>
    public static List<ProcessInfo> Find(IReadOnlyCollection<string> names)
    {
        var found = new List<ProcessInfo>();
        var all = Process.GetProcesses();
        try
        {
            foreach (var p in all)
            {
                try
                {
                    if (names.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                        found.Add(new ProcessInfo(p.ProcessName, p.SessionId, ImagePath((uint)p.Id), IsSuspended(p)));
                }
                catch (InvalidOperationException) { } // exited while we looked
            }
        }
        finally
        {
            foreach (var p in all) p.Dispose();
        }
        return found;
    }

    /// <summary>True when every thread is suspended. On any error: false (counting it as active is the safe side).</summary>
    static bool IsSuspended(Process p)
    {
        try
        {
            var threads = p.Threads;
            if (threads.Count == 0) return false;
            foreach (ProcessThread t in threads)
                if (t.ThreadState != System.Diagnostics.ThreadState.Wait || t.WaitReason != ThreadWaitReason.Suspended) return false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>True when the process runs with an elevated admin token (a filtered admin token has Administrators as deny-only).</summary>
    public static bool IsElevated(uint pid)
    {
        using var process = Open(pid);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, out var token)) throw new IOException("Cannot read the process token");
        using (token)
        using (var identity = new System.Security.Principal.WindowsIdentity(token.DangerousGetHandle()))
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>Active console session id, or -1 when there is none (e.g. while switching users).</summary>
    public static int ConsoleSessionId()
    {
        var id = PInvoke.WTSGetActiveConsoleSessionId();
        return id == uint.MaxValue ? -1 : (int)id;
    }

    public static string? ImagePath(uint pid)
    {
        using var h = Open(pid);
        return ImagePath(h);
    }
}
