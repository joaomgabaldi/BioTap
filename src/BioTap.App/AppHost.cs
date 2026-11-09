using BioTap.App.Overlay;
using BioTap.App.Panel;
using BioTap.App.Tray;
using BioTap.Core;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Microsoft.UI.Dispatching;

namespace BioTap.App;

/// <summary>
/// Wires the pieces together: config → router; service connection → router and tray; router → overlay.
/// Everything that touches UI runs on the UI dispatcher.
/// </summary>
sealed class AppHost(DispatcherQueue ui)
{
    const string ServiceExe = @"%ProgramFiles%\BioTap\BioTap.Service.exe";

    readonly string configPath = Path.Combine(Program.DataFolder, "config.json");
    readonly CancellationTokenSource stop = new();
    readonly ProblemNotifier notifier = new();
    readonly ShellLauncher launcher = new();
    PresetCatalog presets = null!;
    ServiceConnection connection = null!;
    TapRouter router = null!;
    OverlayWindow overlay = null!;
    TrayIcon tray = null!;
    PanelWindow panel = null!;
    volatile LoadResult loaded = new(AppConfig.Default, false, [], new HashSet<Guid>());
    ConnectionState connectionState = ConnectionState.Connecting;
    SensorState? sensorState;

    Settings.SettingsWindow? settings;

    public PresetCatalog Presets => presets;
    public LoadResult Loaded => loaded;
    public string ConfigPath => configPath;
    public ServiceConnection Connection => connection;
    public TapRouter Router => router;
    public string StatusText => PanelModel.TrayTooltip(connectionState, sensorState);

    public void Start()
    {
        presets = new PresetCatalog([new OpenTargetPreset(launcher), new RunCommandPreset(launcher)]);
        Reload();

        overlay = new OverlayWindow();
        panel = new PanelWindow();
        panel.ItemClicked += id => OpenSettings(id);
        panel.GearClicked += () => OpenSettings(null);

        tray = new TrayIcon();
        tray.Clicked += TogglePanel;
        tray.SettingsRequested += () => OpenSettings(null);
        tray.ExitRequested += Exit;

        router = new TapRouter(() => loaded.Config, presets);
        router.Outcome += o => ui.TryEnqueue(() =>
        {
            var c = loaded.Config;
            if (c.Overlay.Enabled) overlay.Show(o, TimeSpan.FromMilliseconds(c.Overlay.DurationMs));
        });

        connection = new ServiceConnection(() => new PipeClient(PipeServerName, ServiceExe), TimeProvider.System);
        connection.Tapped += router.OnTaps;
        connection.StateChanged += s => ui.TryEnqueue(() =>
        {
            connectionState = s;
            if (s != ConnectionState.Connected) sensorState = null; // the old sensor status is unknown until the service reports again
            UpdateTray();
        });
        connection.SensorStateChanged += s => ui.TryEnqueue(() => { sensorState = s; UpdateTray(); });
        _ = connection.RunAsync(stop.Token);

        UpdateTray();
        if (loaded.Errors.Count > 0) tray.Notify("BioTap: configuração", loaded.Errors[0]);
    }

    const string PipeServerName = "BioTap";

    /// <summary>Reads config.json again (after the settings window saved it).</summary>
    public void Reload()
    {
        loaded = ConfigStore.Load(configPath, AclGuard.IsWritableOnlyByAdmins, presets);
        foreach (var e in loaded.Errors) Program.Log.Write("WARN", "Config: " + e);
    }

    /// <summary>One preferences window; reopened after it is closed. <paramref name="actionId"/> opens that action's editor.</summary>
    void OpenSettings(Guid? actionId)
    {
        try
        {
            if (settings is null)
            {
                settings = new Settings.SettingsWindow(this);
                settings.Closed += () => settings = null;
            }
            settings.Show(actionId);
        }
        catch (Exception e)
        {
            // A broken preferences window must never take the tray app (and so the taps) down with it
            Program.Log.Write("ERROR", $"Open settings: {e}");
            settings = null;
            tray.Notify("BioTap", "Não foi possível abrir as preferências.");
        }
    }

    public void ShowPanel() => panel.Show(PanelModel.Items(loaded.Config));
    void TogglePanel() => panel.Toggle(PanelModel.Items(loaded.Config));

    void UpdateTray()
    {
        var text = PanelModel.TrayTooltip(connectionState, sensorState);
        tray.SetTooltip(text);
        if (notifier.ShouldNotify(connectionState, sensorState)) tray.Notify("BioTap", text["BioTap: ".Length..]);
    }

    void Exit()
    {
        stop.Cancel();
        tray.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
