using System.Diagnostics;
using BioTap.Core;

namespace BioTap.Service;

public static class HelloWatcher
{
    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>One process snapshot per call; only Hello-named processes get their image path read (spec §2).</summary>
    public static bool IsHelloActive()
    {
        var all = Process.GetProcesses();
        try
        {
            var candidates = new List<ProcessInfo>();
            foreach (var p in all)
            {
                try
                {
                    if (FocusPolicy.HelloProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(new ProcessInfo(p.ProcessName, p.SessionId, NativeProcess.ImagePath((uint)p.Id)));
                }
                catch (InvalidOperationException) { } // exited while we looked
            }
            return HelloDetector.IsHelloActive(candidates, NativeProcess.ConsoleSessionId(), WindowsDir);
        }
        finally
        {
            foreach (var p in all) p.Dispose();
        }
    }
}
