using BioTap.Core;

namespace BioTap.Service;

public static class HelloWatcher
{
    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>One cheap process snapshot per call (spec §2).</summary>
    public static bool IsHelloActive()
        => HelloDetector.IsHelloActive(NativeProcess.Find(FocusPolicy.HelloProcesses), NativeProcess.ConsoleSessionId(), WindowsDir);
}
