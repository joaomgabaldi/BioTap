using BioTap.Core.Actions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Os;

/// <summary>IWindows through Win32. Never logs window titles.</summary>
sealed class Win32Windows : IWindows
{
    static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public nint Foreground()
    {
        var h = PInvoke.GetForegroundWindow();
        if (h.IsNull || h == PInvoke.GetShellWindow() || IsShellClass(h)) return 0;
        return (nint)h;
    }

    public void ToggleTopmost(nint window)
    {
        var h = (HWND)window;
        var topmost = (PInvoke.GetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & (int)WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0;
        PInvoke.SetWindowPos(h, (HWND)new IntPtr(topmost ? -2 : -1), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    public void MinimizeAllExcept(nint window)
    {
        var shell = PInvoke.GetShellWindow();
        List<HWND> targets = [];
        PInvoke.EnumWindows((h, _) =>
        {
            if ((nint)h != window && h != shell && Minimizable(h)) targets.Add(h);
            return true;
        }, 0);
        foreach (var h in targets) PInvoke.ShowWindow(h, SHOW_WINDOW_CMD.SW_MINIMIZE);
    }

    public void Lock() => PInvoke.LockWorkStation();

    static bool Minimizable(HWND h)
    {
        if (!PInvoke.IsWindowVisible(h) || PInvoke.IsIconic(h)) return false;
        if ((PInvoke.GetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0) return false;
        if (PInvoke.GetWindowTextLength(h) == 0 || IsShellClass(h)) return false;
        uint pid;
        _ = PInvoke.GetWindowThreadProcessId(h, out pid);
        return pid != Environment.ProcessId;
    }

    static bool IsShellClass(HWND h)
    {
        Span<char> buf = stackalloc char[64];
        var n = PInvoke.GetClassName(h, buf);
        return n > 0 && ShellClasses.Contains(new string(buf[..n]));
    }
}
