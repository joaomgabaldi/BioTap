using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using BioTap.Core.Text;

namespace BioTap.Core.Actions;

/// <summary>
/// Starts things the way a double click in Explorer would: through the desktop's Shell.Application, which runs
/// inside the non-elevated explorer.exe, so the child gets the user's normal (filtered) token (spec §3).
/// Before every launch it checks that the window it reached belongs to a non-elevated %windir%\explorer.exe:
/// with Explorer gone, COM could otherwise start a stand-in server with this app's elevated token.
/// Late-bound IDispatch calls only: no pointers, no hand-written interop.
/// </summary>
public sealed class ShellLauncher : ILauncher
{
    static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    const int SwcDesktop = 8;
    const int SwfoNeedDispatch = 1;

    public void OpenUnelevated(string file, string? args, string? workDir, bool showWindow = true)
    {
        object? windows = null, desktop = null, view = null, shell = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: true)!;
            windows = Activator.CreateInstance(type);
            object location = 0; // CSIDL_DESKTOP
            object root = Type.Missing;
            var hwnd = 0;
            desktop = ((dynamic)windows!).FindWindowSW(ref location, ref root, SwcDesktop, ref hwnd, SwfoNeedDispatch);
            if (desktop is null || hwnd == 0) throw new PresetException(Strings.Current.ExplorerNotRunning);
            if (CheckHostWindow(hwnd) is { } problem) throw new PresetException(problem);

            view = ((dynamic)desktop).Document;   // IShellFolderViewDual of the desktop
            shell = ((dynamic)view!).Application; // IShellDispatch2, living in explorer.exe
            ((dynamic)shell!).ShellExecute(file, args ?? "", workDir ?? "", "open", showWindow ? 1 : 0);
        }
        catch (Exception e) when (e is not PresetException)
        {
            // Never fall back to an elevated start: failing is the safe outcome
            throw new PresetException(Strings.Current.ExplorerOpenFailed);
        }
        finally
        {
            foreach (var o in new[] { shell, view, desktop, windows })
                if (o is not null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
        }
    }

    static string? CheckHostWindow(int hwnd)
    {
        if (PInvoke.GetWindowThreadProcessId(new HWND(hwnd), out var pid) == 0 || pid == 0) return Strings.Current.ExplorerNotFound;
        try
        {
            return CheckShellHost(NativeProcess.IsElevated(pid), NativeProcess.ImagePath(pid), Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }
        catch (Exception)
        {
            return Strings.Current.ExplorerNotVerifiable;
        }
    }

    /// <summary>null when the shell host is the user's normal Explorer; otherwise why it must not be used.</summary>
    internal static string? CheckShellHost(bool elevated, string? image, string windowsDir)
    {
        if (image is null) return Strings.Current.ExplorerNotVerifiable;
        if (!string.Equals(Path.GetFullPath(image), Path.Combine(windowsDir, "explorer.exe"), StringComparison.OrdinalIgnoreCase))
            return Strings.Current.ShellNotExplorer;
        return elevated ? Strings.Current.ExplorerElevated : null;
    }

    public void RunElevated(string file, string args, bool showWindow)
        // ShellExecute (not CreateProcess): no handle inheritance; known working directory
        => Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.SystemDirectory,
            WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
        })?.Dispose();
}
