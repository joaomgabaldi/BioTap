using Microsoft.UI.Xaml;

namespace BioTap.App;

public partial class App : Application
{
    public App() => InitializeComponent();

    /// <summary>Raised on the UI thread when another BioTap.exe was started (spec §4: opening the exe shows the panel).</summary>
    public event Action? ShowPanelRequested;

    internal void RequestShowPanel() => ShowPanelRequested?.Invoke();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // No main window: the app lives in the tray (Task 7).
    }
}
