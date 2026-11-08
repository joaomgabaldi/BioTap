using System.Diagnostics;
using BioTap.Core;

namespace BioTap.Service;

public static class HelloWatcher
{
    /// <summary>True while any process that may show a Windows Hello prompt is running (spec §2).</summary>
    public static bool IsHelloActive()
    {
        foreach (var name in FocusPolicy.HelloProcesses)
        {
            var procs = Process.GetProcessesByName(name);
            foreach (var p in procs) p.Dispose();
            if (procs.Length > 0) return true;
        }
        return false;
    }
}
