using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BioTap.App;

public partial class App : Application
{
    AppHost? host;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Program.Log.Write("ERROR", $"Unhandled UI: {e.Exception}"); // UI code exceptions carry no user secrets
            e.Handled = true; // a bug in one window must not kill the tray app
        };
        // The app lives in the tray: closing the panel or preferences must not end the process
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    /// <summary>Another BioTap.exe was started (spec §4: opening the exe shows the panel). Call on the UI thread.</summary>
    internal void RequestShowPanel()
    {
        Program.Log.Write("INFO", "Show panel requested");
        host?.ShowPanel();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        host = new AppHost(DispatcherQueue.GetForCurrentThread());
        host.Start();
        Program.Log.Write("INFO", "BioTap started");
    }
}
