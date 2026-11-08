using Microsoft.UI.Xaml;

namespace BioTap.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Program.Log.Write("ERROR", $"Unhandled UI {e.Exception.GetType().Name}");
        // The app lives in the tray: closing the panel or preferences must not end the process
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    /// <summary>Raised on the UI thread when another BioTap.exe was started (spec §4: opening the exe shows the panel).</summary>
    public event Action? ShowPanelRequested;

    internal void RequestShowPanel()
    {
        Program.Log.Write("INFO", "Show panel requested");
        ShowPanelRequested?.Invoke();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // No main window: the tray and panel come in Task 7.
    }
}
