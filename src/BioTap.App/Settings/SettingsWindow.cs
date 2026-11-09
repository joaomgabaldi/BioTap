using System.Text.Json.Nodes;
using BioTap.App.Os;
using BioTap.App.Overlay;
using BioTap.App.Shell;
using BioTap.Core;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        nav.SelectionChanged += (_, e) => Ui.Run(() => ShowPage((e.SelectedItem as NavigationViewItem)?.Tag as string), "settings page");
        // One layout for the window's lifetime: the banner keeps a single parent; only the page changes
        nav.Content = new ScrollViewer { Content = new StackPanel { Padding = new Thickness(32, 24, 32, 32), Spacing = 12, Children = { banner, pageHost } } };

        window = new Window { Title = "BioTap — Preferências", SystemBackdrop = new MicaBackdrop(), Content = nav };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 680));
        window.Closed += (_, _) => Ui.Run(() => Closed?.Invoke(), "settings closed");
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        // NavigationView refuses SelectedItem before it is loaded (COMException 0x80070490): select once it is
        nav.Loaded += (_, _) => loaded.TrySetResult();
    }

    readonly TaskCompletionSource loaded = new();

    public async void Show(Guid? actionId)
    {
        try
        {
            window.Activate();
            PInvoke.SetForegroundWindow(hwnd);
            await loaded.Task; // completes after the first Activate puts the content on screen
            if (nav.SelectedItem is null) nav.SelectedItem = nav.MenuItems[0];
            if (dialogOpen) return; // an editor is already open: just bring the window up
            saved = host.Loaded.Config; // pick up a config reloaded since this window was built
            if (actionId is not { } id) return;
            nav.SelectedItem = nav.MenuItems[0];
            if (saved.Actions.FirstOrDefault(a => a.Id == id) is { } action) await EditAsync(action, isNew: false);
        }
        catch (Exception e)
        {
            Program.Log.Write("ERROR", $"Settings show: {e}");
        }
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
            item.Click += (_, _) => Ui.RunAsync(() => EditAsync(ActionEditor.NewAction(saved, preset), isNew: true), "new action");
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
        enabled.Toggled += (_, _) => Ui.RunAsync(async () =>
        {
            await SaveAsync(ActionEditor.WithAction(saved, a with { Enabled = enabled.IsOn }));
            ShowPage("actions"); // redraw with the real state (saved or unchanged)
        }, "toggle action");
        var edit = new Button { Content = "Editar" };
        edit.Click += (_, _) => Ui.RunAsync(() => EditAsync(a, isNew: false), "edit action");
        var test = new Button { Content = "Testar" };
        test.Click += (_, _) => Ui.Run(() => host.Router.Test(a), "test action");
        var remove = new Button { Content = "Excluir" };
        remove.Click += (_, _) => Ui.RunAsync(async () =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot, Title = $"Excluir \"{a.Name}\"?", Content = $"A sequência de {a.Taps} toques deixará de fazer algo.",
                PrimaryButtonText = "Excluir", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(confirm) == ContentDialogResult.Primary && await SaveAsync(ActionEditor.Without(saved, a.Id)))
            {
                IconCache.Remove(Program.DataFolder, a.Id);
                ShowPage("actions");
            }
        }, "delete action");
        var preset = host.Presets.Get(a.Preset)?.DisplayName ?? a.Preset;
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = Theme.Brush("CardBackgroundFillColorDefaultBrush", Theme.Card) };
        foreach (var w in new[] { 40, 90, 0, 0, 0, 0, 0 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : (grid.ColumnDefinitions.Count == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto) });
        // Spec §5: an action the loader disabled as invalid says so (otherwise it just looks switched off)
        var invalid = host.Loaded.InvalidActions.Contains(a.Id);
        Add(grid, 0, invalid ? new FontIcon { Glyph = Glyphs.Warning, FontSize = 22 } : Icons.Element(a.Icon, 22));
        Add(grid, 1, new TextBlock { Text = $"{a.Taps} toques", VerticalAlignment = VerticalAlignment.Center });
        Add(grid, 2, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = a.Name }, new TextBlock { Text = invalid ? "Inválida — abra Editar para corrigir" : preset, FontSize = 12, Opacity = 0.7 } } });
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
        // Only "Abrir" can show the program's own icon; it is the default for new actions
        var hasProgramIcon = action.Preset == "openTarget";
        if (hasProgramIcon)
            icon.Items.Add(new ComboBoxItem { Tag = "exe", Content = new TextBlock { Text = "Ícone do programa" } });
        foreach (var (glyph, label) in IconChoices)
            icon.Items.Add(new ComboBoxItem { Tag = glyph, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = Glyphs.For(new(OutcomeKind.Ran, "", null, new IconRef("glyph", glyph))) }, new TextBlock { Text = label } } } });
        // A glyph outside the list (set in config.json) stays as it is unless the user picks another
        if (action.Icon.Kind != "exe" && !IconChoices.Any(c => c.Glyph == action.Icon.Value))
            icon.Items.Add(new ComboBoxItem { Tag = action.Icon.Value, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = action.Icon.GlyphText() }, new TextBlock { Text = "Atual" } } } });
        var wantProgramIcon = hasProgramIcon && (isNew || action.Icon.Kind == "exe");
        var selectedTag = wantProgramIcon ? "exe" : action.Icon.Value;
        icon.SelectedItem = icon.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selectedTag) ?? icon.Items[0];
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        ContentDialog? dialogRef = null;
        UIElement? mainContent = null;
        // A sequence step editor takes over the one dialog; Salvar/Testar stay off so half-edited state is never saved
        void Swap(UIElement? step)
        {
            dialogRef!.Content = step ?? mainContent;
            dialogRef.IsPrimaryButtonEnabled = dialogRef.IsSecondaryButtonEnabled = step is null;
        }
        var fields = PresetEditors.Build(action.Preset, action.Params, window.AppWindow.Id, host.Presets, Swap);

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

        dialogRef = dialog;
        mainContent = dialog.Content as UIElement;

        ActionConfig Edited() => action with
        {
            Taps = double.IsNaN(taps.Value) ? 0 : (int)Math.Round(taps.Value),
            Name = string.IsNullOrWhiteSpace(name.Text) ? host.Presets.Get(action.Preset)?.DisplayName ?? action.Preset : name.Text.Trim(),
            Icon = (string)((ComboBoxItem)icon.SelectedItem).Tag is "exe"
                ? (action.Icon.Kind == "exe" ? action.Icon : new IconRef("exe", action.Id.ToString("N"))) // real value set by Store on save
                : new IconRef("glyph", (string)((ComboBoxItem)icon.SelectedItem).Tag),
            Params = fields.Read(),
        };

        dialog.PrimaryButtonClick += (_, e) => Ui.Run(() =>
        {
            e.Cancel = true; // stays open unless the edit is valid
            if (ActionEditor.Check(saved, Edited(), host.Presets) is { } problem)
            {
                error.Message = problem;
                error.IsOpen = true;
                return;
            }
            e.Cancel = false;
        }, "editor save");
        dialog.SecondaryButtonClick += (_, e) => Ui.Run(() =>
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
        }, "editor test");

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var edited = Edited();
        if (edited.Icon.Kind == "exe")
        {
            var target = edited.Params["target"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
            var png = await Task.Run(() => new IconExtractor().ExtractPng(target, 48)); // reads the disk
            edited = edited with { Icon = IconCache.Store(Program.DataFolder, edited.Id, png, host.Presets.Get(edited.Preset)?.DefaultIcon ?? new IconRef("glyph", "E8E5")) };
        }
        if (!await SaveAsync(ActionEditor.WithAction(saved, edited))) return;
        if (edited.Icon.Kind != "exe") IconCache.Remove(Program.DataFolder, edited.Id); // glyph now, or no icon could be read
        ShowPage("actions");
    }

    // ---------- Geral ----------

    UIElement GeneralPage()
    {
        var timeout = new NumberBox { Header = "Tempo para fechar a sequência de toques (ms)", Minimum = TapCounter.MinTimeoutMs, Maximum = TapCounter.MaxTimeoutMs, Value = saved.TapTimeoutMs, SmallChange = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = "Salvar", Style = Theme.Style("AccentButtonStyle") };
        save.Click += (_, _) => Ui.RunAsync(() => SaveAsync(saved with { TapTimeoutMs = double.IsNaN(timeout.Value) ? saved.TapTimeoutMs : (int)Math.Round(timeout.Value) }), "save general");

        var startup = new StartupTask();
        var startWithWindows = new ToggleSwitch { Header = "Iniciar com o Windows" };
        try { startWithWindows.IsOn = startup.IsEnabled(); }
        catch (Exception)
        {
            startWithWindows.IsEnabled = false;
            startWithWindows.Header = "Iniciar com o Windows (não foi possível ler o Agendador de Tarefas)";
        }
        var reverting = false;
        startWithWindows.Toggled += (_, _) => Ui.Run(() =>
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
        }, "startup toggle");

        RefreshStatus();
        (status.Parent as Microsoft.UI.Xaml.Controls.Panel)?.Children.Remove(status); // still a child of the previous Geral page
        return new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Geral", Style = Theme.Style("SubtitleTextBlockStyle") },
                timeout, save, startWithWindows,
                status,
            },
        };
    }

    readonly TextBlock status = new() { Opacity = 0.8 }; // one control: the page is rebuilt, the text follows the connection

    /// <summary>Called by AppHost on the UI thread whenever the service or sensor state changes.</summary>
    public void RefreshStatus() => status.Text = "Estado: " + host.StatusText["BioTap: ".Length..];

    static readonly string InstalledExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BioTap", "App", "BioTap.exe");

    // ---------- Aparência e feedback ----------

    UIElement AppearancePage()
    {
        var show = new ToggleSwitch { Header = "Mostrar o overlay quando uma ação roda", IsOn = saved.Overlay.Enabled };
        var duration = new NumberBox { Header = "Tempo na tela (ms)", Minimum = OverlayConfig.MinDurationMs, Maximum = OverlayConfig.MaxDurationMs, Value = saved.Overlay.DurationMs, SmallChange = 250, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = "Salvar", Style = Theme.Style("AccentButtonStyle") };
        save.Click += (_, _) => Ui.RunAsync(() => SaveAsync(saved with { Overlay = new OverlayConfig(show.IsOn, double.IsNaN(duration.Value) ? saved.Overlay.DurationMs : (int)Math.Round(duration.Value)) }), "save appearance");
        return new StackPanel
        {
            Spacing = 16,
            Children = { new TextBlock { Text = "Aparência e feedback", Style = Theme.Style("SubtitleTextBlockStyle") }, show, duration, save },
        };
    }
}
