using BioTap.App.Shell;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            IconSource = new GeneratedIconSource
            {
                Text = "", // Fingerprint
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Colors.White),
                Size = 32,
            },
            ToolTipText = "BioTap",
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => Clicked?.Invoke()),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Preferências", Command = new RelayCommand(() => SettingsRequested?.Invoke()) },
                    new MenuFlyoutItem { Text = "Sair", Command = new RelayCommand(() => ExitRequested?.Invoke()) },
                },
            },
        };
        icon.ForceCreate();
    }

    public void SetTooltip(string text) => icon.ToolTipText = text;

    /// <summary>A Windows notification from the tray (no toast activation needed for an unpackaged app).</summary>
    public void Notify(string title, string message) => icon.ShowNotification(title, message);

    public void Dispose() => icon.Dispose();
}
