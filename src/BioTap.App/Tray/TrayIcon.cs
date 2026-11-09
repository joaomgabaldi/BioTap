using BioTap.App.Shell;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Tray;

/// <summary>Tray icon: left click toggles the panel; right click shows Preferências / Sair.</summary>
sealed class TrayIcon : IDisposable
{
    readonly TaskbarIcon icon;

    public event Action? Clicked;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        // This app is elevated and Explorer is not: let Explorer's "TaskbarCreated" through, or the icon is lost
        // for good when Explorer restarts (UIPI drops messages from lower integrity)
        PInvoke.ChangeWindowMessageFilter(PInvoke.RegisterWindowMessage("TaskbarCreated"), CHANGE_WINDOW_MESSAGE_FILTER_FLAGS.MSGFLT_ADD);

        icon = new TaskbarIcon
        {
            // A real .ico (fingerprint glyph, 16–256 px); the generated-from-font icon came out blank
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "BioTap.ico")),
            ToolTipText = "BioTap",
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => Safe(Clicked, "tray click")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Preferências", Command = new RelayCommand(() => Safe(SettingsRequested, "tray settings")) },
                    new MenuFlyoutItem { Text = "Sair", Command = new RelayCommand(() => Safe(ExitRequested, "tray exit")) },
                },
            },
        };
        icon.ForceCreate();
    }

    // Tray callbacks come from a native window procedure: an exception escaping here is a fail-fast in combase
    // (it never reaches Application.UnhandledException), so the whole app would close.
    static void Safe(Action? handler, string what)
    {
        try { handler?.Invoke(); }
        catch (Exception e) { Program.Log.Write("ERROR", $"{what}: {e}"); }
    }

    public void SetTooltip(string text) => icon.ToolTipText = text;

    /// <summary>A Windows notification from the tray (no toast activation needed for an unpackaged app).</summary>
    public void Notify(string title, string message) => icon.ShowNotification(title, message);

    public void Dispose() => icon.Dispose();
}
