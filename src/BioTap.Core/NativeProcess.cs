using Microsoft.Win32.SafeHandles;
using Windows.Win32;
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
