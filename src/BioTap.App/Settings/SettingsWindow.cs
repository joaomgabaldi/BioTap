using System.Text.Json.Nodes;
using BioTap.App.Overlay;
using BioTap.App.Shell;
using BioTap.Core;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace BioTap.App.Settings;

/// <summary>
/// Preferences (spec §4): Ações / Geral / Aparência e feedback. All rules live in Core (ActionEditor, ConfigValidator,
/// SettingsSaver); this class only builds controls and calls them. Changes apply when saved.
/// </summary>
sealed class SettingsWindow
{
    static readonly (string Glyph, string Label)[] IconChoices =
    [
        ("E8E5", "Abrir"), ("E756", "Comando"), ("E71D", "Apps"), ("E8B7", "Pasta"), ("E767", "Som"), ("E7F6", "Fone"),
        ("E714", "Vídeo"), ("E774", "Web"), ("E7FC", "Jogo"), ("E72E", "Cadeado"), ("E722", "Câmera"), ("E768", "Play"),
        ("E734", "Estrela"), ("E928", "Digital"),
    ];

    readonly AppHost host;
    readonly Window window;
    readonly HWND hwnd;
    readonly NavigationView nav = new() { PaneDisplayMode = NavigationViewPaneDisplayMode.Left, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsSettingsVisible = false };
    readonly InfoBar banner = new() { IsClosable = true, Margin = new Thickness(0, 0, 0, 12) };
    readonly ContentControl pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly SettingsSaver saver;
    AppConfig saved;
    bool dialogOpen; // WinUI allows one ContentDialog at a time

    public event Action? Closed;

    public SettingsWindow(AppHost host)
    {
        this.host = host;
        saved = host.Loaded.Config;
        saver = new SettingsSaver(host.ConfigPath, host.Presets, AclGuard.IsWritableOnlyByAdmins, host.Connection.SendTimeoutAsync);

        nav.MenuItems.Add(new NavigationViewItem { Content = "Ações", Icon = new FontIcon { Glyph = "\uE8FD" }, Tag = "actions" });
        nav.MenuItems.Add(new NavigationViewItem { Content = "Geral", Icon = new FontIcon { Glyph = "\uE713" }, Tag = "general" });
        nav.MenuItems.Add(new NavigationViewItem { Content = "Aparência e feedback", Icon = new FontIcon { Glyph = "\uE790" }, Tag = "appearance" });
        nav.SelectionChanged += (_, e) => ShowPage((e.SelectedItem as NavigationViewItem)?.Tag as string);
        // One layout for the window's lifetime: the banner keeps a single parent; only the page changes
        nav.Content = new ScrollViewer { Content = new StackPanel { Padding = new Thickness(32, 24, 32, 32), Spacing = 12, Children = { banner, pageHost } } };

        window = new Window { Title = "BioTap — Preferências", SystemBackdrop = new MicaBackdrop(), Content = nav };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 680));
        window.Closed += (_, _) => Closed?.Invoke();
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        nav.SelectedItem = nav.MenuItems[0];
    }

    public async void Show(Guid? actionId)
    {
        window.Activate();
        PInvoke.SetForegroundWindow(hwnd);
        if (dialogOpen) return; // an editor is already open: just bring the window up
        saved = host.Loaded.Config; // pick up a config reloaded since this window was built
        if (actionId is not { } id) return;
        nav.SelectedItem = nav.MenuItems[0];
        if (saved.Actions.FirstOrDefault(a => a.Id == id) is { } action) await EditAsync(action, isNew: false);
    }

    void ShowPage(string? tag)
    {
        saved = host.Loaded.Config;
        pageHost.Content = tag switch { "general" => GeneralPage(), "appearance" => AppearancePage(), _ => ActionsPage() };
        var loaded = host.Loaded;
        if (loaded.ReadOnly)
            Banner(InfoBarSeverity.Error, "A configuração atual não pode ser alterada", string.Join(" ", loaded.Errors));
    }

    void Banner(InfoBarSeverity severity, string title, string? message)
    {
        banner.Severity = severity;
        banner.Title = title;
        banner.Message = message ?? "";
        banner.IsOpen = true;
    }

    async Task<bool> SaveAsync(AppConfig next)
    {
        var outcome = await saver.SaveAsync(saved, next);
        if (!outcome.Saved)
        {
            Banner(InfoBarSeverity.Error, "Não foi salvo", outcome.Message);
            return false;
        }
        host.Reload();
        saved = host.Loaded.Config;
        if (outcome.Message is { } warning) Banner(InfoBarSeverity.Warning, "Salvo com aviso", warning);
        else Banner(InfoBarSeverity.Success, "Salvo", null);
        return true;
    }

    /// <summary>Shows a ContentDialog unless one is already open (WinUI throws on a second).</summary>
    async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (dialogOpen || window.Content.XamlRoot is null) return ContentDialogResult.None;
        dialogOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { dialogOpen = false; }
    }

    // ---------- Ações ----------

    UIElement ActionsPage()
    {
        var page = new StackPanel { Spacing = 8 };
        var add = new DropDownButton { Content = "Nova ação", Flyout = new MenuFlyout() };
        foreach (var preset in host.Presets.All)
        {
            var item = new MenuFlyoutItem { Text = preset.DisplayName };
            item.Click += async (_, _) => await EditAsync(ActionEditor.NewAction(saved, preset), isNew: true);
            ((MenuFlyout)add.Flyout).Items.Add(item);
        }
        page.Children.Add(new TextBlock { Text = "Ações", Style = Theme.Style("SubtitleTextBlockStyle") });
        page.Children.Add(add);
        if (saved.Actions.Count == 0) page.Children.Add(new TextBlock { Text = PanelModel.EmptyText, Opacity = 0.7 });
        foreach (var a in saved.Actions.OrderBy(a => a.Taps)) page.Children.Add(ActionRow(a));
        return page;
    }

    UIElement ActionRow(ActionConfig a)
    {
        var enabled = new ToggleSwitch { IsOn = a.Enabled, OnContent = "Ligada", OffContent = "Desligada", MinWidth = 0 };
        // Turning an action on or off saves at once (a single switch has nothing else to confirm)
        enabled.Toggled += async (_, _) =>
        {
            if (await SaveAsync(ActionEditor.WithAction(saved, a with { Enabled = enabled.IsOn }))) ShowPage("actions");
            else ShowPage("actions"); // redraw with the real, unchanged state
        };
        var edit = new Button { Content = "Editar" };
        edit.Click += async (_, _) => await EditAsync(a, isNew: false);
        var test = new Button { Content = "Testar" };
        test.Click += (_, _) => host.Router.Test(a);
        var remove = new Button { Content = "Excluir" };
        remove.Click += async (_, _) =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot, Title = $"Excluir \"{a.Name}\"?", Content = $"A sequência de {a.Taps} toques deixará de fazer algo.",
                PrimaryButtonText = "Excluir", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(confirm) == ContentDialogResult.Primary && await SaveAsync(ActionEditor.Without(saved, a.Id))) ShowPage("actions");
        };
        var preset = host.Presets.Get(a.Preset)?.DisplayName ?? a.Preset;
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = Theme.Brush("CardBackgroundFillColorDefaultBrush", Theme.Card) };
        foreach (var w in new[] { 40, 90, 0, 0, 0, 0, 0 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : (grid.ColumnDefinitions.Count == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto) });
        Add(grid, 0, new FontIcon { Glyph = Glyphs.For(new(OutcomeKind.Ran, "", null, a.Icon)), FontSize = 22 });
        Add(grid, 1, new TextBlock { Text = $"{a.Taps} toques", VerticalAlignment = VerticalAlignment.Center });
        Add(grid, 2, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = a.Name }, new TextBlock { Text = preset, FontSize = 12, Opacity = 0.7 } } });
        Add(grid, 3, enabled);
        Add(grid, 4, edit);
        Add(grid, 5, test);
        Add(grid, 6, remove);
        return grid;
    }

    static void Add(Grid g, int column, FrameworkElement e)
    {
        Grid.SetColumn(e, column);
        e.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(e);
    }

    async Task EditAsync(ActionConfig action, bool isNew)
    {
        var taps = new NumberBox { Header = "Toques", Minimum = 1, Maximum = Protocol.MaxTaps, Value = action.Taps, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1 };
        var name = new TextBox { Header = "Nome (aparece no painel)", Text = action.Name, MaxLength = 80 };
        var icon = new ComboBox { Header = "Ícone", MinWidth = 220 };
        foreach (var (glyph, label) in IconChoices)
            icon.Items.Add(new ComboBoxItem { Tag = glyph, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = Glyphs.For(new(OutcomeKind.Ran, "", null, new IconRef("glyph", glyph))) }, new TextBlock { Text = label } } } });
        icon.SelectedItem = icon.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == action.Icon.Value) ?? icon.Items[0];
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var fields = PresetFields(action.Preset, action.Params);

        var dialog = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot,
            Title = isNew ? "Nova ação" : "Editar ação",
            PrimaryButtonText = "Salvar",
            SecondaryButtonText = "Testar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, MinWidth = 460, Children = { error, taps, name, icon, fields.Panel } } },
        };

        ActionConfig Edited() => action with
        {
            Taps = double.IsNaN(taps.Value) ? 0 : (int)Math.Round(taps.Value),
            Name = string.IsNullOrWhiteSpace(name.Text) ? host.Presets.Get(action.Preset)?.DisplayName ?? action.Preset : name.Text.Trim(),
            Icon = new IconRef("glyph", (string)((ComboBoxItem)icon.SelectedItem).Tag),
            Params = fields.Read(),
        };

        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (ActionEditor.Check(saved, Edited(), host.Presets) is { } problem)
            {
                error.Message = problem;
                error.IsOpen = true;
                e.Cancel = true;
            }
        };
        dialog.SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true; // keep the dialog open while testing
            var edited = Edited();
            if (host.Presets.Get(edited.Preset)?.ValidateParams(edited.Params) is { } problem)
            {
                error.Message = problem;
                error.IsOpen = true;
                return;
            }
            error.IsOpen = false;
            host.Router.Test(edited);
        };

        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary && await SaveAsync(ActionEditor.WithAction(saved, Edited())))
            ShowPage("actions");
    }

    (UIElement Panel, Func<JsonObject> Read) PresetFields(string preset, JsonObject p)
    {
        string S(string key) => p[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        bool B(string key, bool fallback) => p[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

        if (preset == "runCommand")
        {
            var shell = new ComboBox { Header = "Shell", Items = { "powershell", "cmd" }, SelectedItem = S("shell") == "cmd" ? "cmd" : "powershell" };
            var command = new TextBox { Header = "Comando", Text = S("command"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var show = new ToggleSwitch { Header = "Mostrar janela", IsOn = B("showWindow", true) };
            var admin = new ToggleSwitch { Header = "Executar como administrador", IsOn = B("runAsAdmin", false) };
            return (new StackPanel { Spacing = 12, Children = { shell, command, show, admin } }, () => new JsonObject
            {
                ["shell"] = (string)shell.SelectedItem, ["command"] = command.Text, ["showWindow"] = show.IsOn, ["runAsAdmin"] = admin.IsOn,
            });
        }

        // openTarget
        var target = new TextBox { Header = "Programa, arquivo ou URL", Text = S("target"), PlaceholderText = @"C:\Program Files\...\app.exe  ou  https://..." };
        var browse = new Button { Content = "Procurar…" };
        browse.Click += async (_, _) =>
        {
            // Windows App SDK picker: unlike the WinRT one, it works in an elevated app
            try
            {
                var picker = new FileOpenPicker(window.AppWindow.Id) { ViewMode = PickerViewMode.List };
                picker.FileTypeFilter.Add("*");
                if (await picker.PickSingleFileAsync() is { } file) target.Text = file.Path;
            }
            catch (Exception e)
            {
                Program.Log.Write("ERROR", "File picker: " + e.GetType().Name);
            }
        };
        var args = new TextBox { Header = "Argumentos (opcional)", Text = S("args") };
        return (new StackPanel { Spacing = 12, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { target, browse } }, args } }, () =>
        {
            var o = new JsonObject { ["target"] = target.Text.Trim() };
            if (!string.IsNullOrWhiteSpace(args.Text)) o["args"] = args.Text;
            return o;
        });
    }

    // ---------- Geral ----------

    UIElement GeneralPage()
    {
        var timeout = new NumberBox { Header = "Tempo para fechar a sequência de toques (ms)", Minimum = TapCounter.MinTimeoutMs, Maximum = TapCounter.MaxTimeoutMs, Value = saved.TapTimeoutMs, SmallChange = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = "Salvar", Style = Theme.Style("AccentButtonStyle") };
        save.Click += async (_, _) => await SaveAsync(saved with { TapTimeoutMs = double.IsNaN(timeout.Value) ? saved.TapTimeoutMs : (int)Math.Round(timeout.Value) });

        var startup = new StartupTask();
        var startWithWindows = new ToggleSwitch { Header = "Iniciar com o Windows" };
        try { startWithWindows.IsOn = startup.IsEnabled(); }
        catch (Exception)
        {
            startWithWindows.IsEnabled = false;
            startWithWindows.Header = "Iniciar com o Windows (não foi possível ler o Agendador de Tarefas)";
        }
        var reverting = false;
        startWithWindows.Toggled += (_, _) =>
        {
            if (reverting) return;
            try
            {
                // Only the installed copy: a highest-privileges logon task must never point at a user-writable exe
                if (startWithWindows.IsOn) startup.Enable(InstalledExe);
                else startup.Disable();
            }
            catch (Exception e)
            {
                Program.Log.Write("ERROR", "Startup task: " + e.GetType().Name);
                Banner(InfoBarSeverity.Error, "Não foi possível alterar a inicialização com o Windows",
                    e is InvalidOperationException ? e.Message : null);
                reverting = true;
                startWithWindows.IsOn = !startWithWindows.IsOn; // show the real state
                reverting = false;
            }
        };

        return new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Geral", Style = Theme.Style("SubtitleTextBlockStyle") },
                timeout, save, startWithWindows,
                new TextBlock { Text = "Estado: " + host.StatusText["BioTap: ".Length..], Opacity = 0.8 },
            },
        };
    }

    static readonly string InstalledExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BioTap", "App", "BioTap.exe");

    // ---------- Aparência e feedback ----------

    UIElement AppearancePage()
    {
        var show = new ToggleSwitch { Header = "Mostrar o overlay quando uma ação roda", IsOn = saved.Overlay.Enabled };
        var duration = new NumberBox { Header = "Tempo na tela (ms)", Minimum = OverlayConfig.MinDurationMs, Maximum = OverlayConfig.MaxDurationMs, Value = saved.Overlay.DurationMs, SmallChange = 250, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = "Salvar", Style = Theme.Style("AccentButtonStyle") };
        save.Click += async (_, _) => await SaveAsync(saved with { Overlay = new OverlayConfig(show.IsOn, double.IsNaN(duration.Value) ? saved.Overlay.DurationMs : (int)Math.Round(duration.Value)) });
        return new StackPanel
        {
            Spacing = 16,
            Children = { new TextBlock { Text = "Aparência e feedback", Style = Theme.Style("SubtitleTextBlockStyle") }, show, duration, save },
        };
    }
}
