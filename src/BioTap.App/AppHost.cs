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
    // Not %ProgramFiles%: the environment of this elevated process comes from the user (HKCU\Environment)
    static readonly string ServiceExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BioTap", "BioTap.Service.exe");

    readonly string configPath = Path.Combine(Program.DataFolder, "config.json");
    readonly CancellationTokenSource stop = new();
    readonly ProblemNotifier notifier = new();
    readonly ShellLauncher launcher = new();
    readonly Os.SendInputKeyboard keyboard = new();
    readonly Os.Win32Windows windows = new();
    readonly Os.WindowsAudio audio = new();
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
    public bool SensorReady => connectionState == ConnectionState.Connected && sensorState == SensorState.Ok;
    public bool StatusProblem => PanelModel.IsProblem(connectionState, sensorState);

    public void Start()
    {
        presets = new PresetCatalog(
        [
            new OpenTargetPreset(launcher), new RunCommandPreset(launcher),
            new ShortcutPreset(keyboard),
            new KeyPreset("copy", "Copiar", "E8C8", keyboard, Vk.Control, 0x43),
            new KeyPreset("paste", "Colar", "E77F", keyboard, Vk.Control, 0x56),
            new KeyPreset("mute", "Silenciar ou reativar o som", "E74F", keyboard, Vk.VolumeMute),
            new KeyPreset("playPause", "Tocar ou pausar", "E768", keyboard, Vk.MediaPlayPause),
            new VolumeStepPreset(keyboard, up: true), new VolumeStepPreset(keyboard, up: false),
            new TypeTextPreset(keyboard),
            new TopmostPreset(windows), new MinimizeOthersPreset(windows), new LockPreset(windows),
            new AudioCyclePreset(audio), new VolumeSetPreset(audio),
            new ScreenshotPreset(new Os.ScreenCapture(ui), windows, () => DateTime.Now),
            new QrCodePreset(new Os.WinRtClipboard(ui), new Qr.QrWindow(ui)),
            new SequencePreset(() => presets, TimeProvider.System),
        ]);
        Reload();

        overlay = new OverlayWindow();
        panel = new PanelWindow();
        // Mesmo caminho de um toque; o painel já se escondeu, então presets de janela agem na janela que estava na frente
        panel.ItemClicked += id => { if (loaded.Config.Actions.FirstOrDefault(a => a.Id == id && a.Enabled) is { } a) router.Test(a); };
        panel.GearClicked += OpenSettings;

        tray = new TrayIcon();
        tray.Clicked += TogglePanel;
        tray.SettingsRequested += OpenSettings;
        tray.ExitRequested += Exit;

        router = new TapRouter(() => loaded.Config, presets);
        router.Outcome += o => ui.TryEnqueue(() => Shell.Ui.Run(() =>
        {
            var c = loaded.Config;
            if (c.Overlay.Enabled) overlay.Show(o, TimeSpan.FromMilliseconds(c.Overlay.DurationMs), panel.Bounds);
        }, "overlay"));

        connection = new ServiceConnection(() => new PipeClient(PipeServerName, ServiceExe), TimeProvider.System);
        connection.Tapped += router.OnTaps;
        connection.StateChanged += s => ui.TryEnqueue(() => Shell.Ui.Run(() =>
        {
            connectionState = s;
            if (s != ConnectionState.Connected) sensorState = null; // the old sensor status is unknown until the service reports again
            UpdateTray();
        }, "connection state"));
        connection.SensorStateChanged += s => ui.TryEnqueue(() => Shell.Ui.Run(() => { sensorState = s; UpdateTray(); }, "sensor state"));
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

    /// <summary>One preferences window; reopened after it is closed.</summary>
    void OpenSettings()
    {
        try
        {
            if (settings is null)
            {
                settings = new Settings.SettingsWindow(this);
                settings.Closed += () => settings = null;
            }
            settings.Show();
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
        settings?.RefreshStatus();
        if (notifier.ShouldNotify(connectionState, sensorState)) tray.Notify("BioTap", text["BioTap: ".Length..]);
    }

    void Exit()
    {
        stop.Cancel();
        tray.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
