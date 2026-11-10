using System.Text.Json.Nodes;
using BioTap.App.Os;
using BioTap.App.Overlay;
using BioTap.App.Shell;
using BioTap.Core;
using BioTap.Core.Actions;
using BioTap.Core.Config;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Win32;
using Windows.Win32.Foundation;
using BioTap.Core.Text;

namespace BioTap.App.Settings;

/// <summary>
/// Preferences (approved layout docs/mockups/preferencias.html, lead/lilac palette): Ações / Geral / Aparência e
/// feedback. All rules live in Core (ActionEditor, ConfigValidator, SettingsSaver); this class only builds
/// controls and calls them. Save policy: switches in the action list and "Iniciar com o Windows" apply at once;
/// the other fields of a page are saved by that page's Salvar button (as before the redesign).
/// </summary>
sealed class SettingsWindow
{
    static readonly (string Glyph, Func<Strings, string> Label)[] IconChoices =
    [
        ("E8E5", s => s.IconOpen), ("E756", s => s.IconCommand), ("E71D", s => s.IconApps), ("E8B7", s => s.IconFolder), ("E767", s => s.IconSound), ("E7F6", s => s.IconHeadphones),
        ("E714", s => s.IconVideo), ("E774", s => s.IconWeb), ("E7FC", s => s.IconGame), ("E72E", s => s.IconLock), ("E722", s => s.IconCamera), ("E768", s => s.IconPlay),
        ("E734", s => s.IconStar), ("E928", s => s.IconFingerprint),
    ];

    /// <summary>"Nova ação" menu groups; a preset missing here still shows, under "Outros".</summary>
    static readonly (Func<Strings, string> Name, string[] Presets)[] Categories =
    [
        (s => s.CatOpenRun, ["openTarget", "runCommand", "sequence"]),
        (s => s.CatKeyboard, ["shortcut", "copy", "paste", "typeText"]),
        (s => s.CatSound, ["mute", "playPause", "volumeUp", "volumeDown", "volumeSet", "audioCycle"]),
        (s => s.CatWindows, ["topmost", "minimizeOthers", "lock", "screenshot", "qrCode"]),
    ];

    const string FingerprintGlyph = ""; // the app's own mark (same glyph as the tray icon)

    readonly AppHost host;
    readonly Window window;
    readonly HWND hwnd;
    readonly NavigationView nav = new()
    {
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        IsPaneToggleButtonVisible = false,
        IsSettingsVisible = false,
        OpenPaneLength = 215,
    };
    readonly InfoBar banner = new() { IsClosable = true, Margin = new Thickness(0, 0, 0, 12) };
    readonly ContentControl pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly Ellipse statusDot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock statusText = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
    readonly SettingsSaver saver;
    AppConfig saved;
    bool dialogOpen; // WinUI allows one ContentDialog at a time

    public event Action? Closed;

    public SettingsWindow(AppHost host)
    {
        this.host = host;
        saved = host.Loaded.Config;
        saver = new SettingsSaver(host.ConfigPath, host.Presets, AclGuard.IsWritableOnlyByAdmins, host.Connection.SendTimeoutAsync);

        nav.MenuItems.Add(NavItem(Strings.Current.NavActions, "", "actions"));
        nav.MenuItems.Add(NavItem(Strings.Current.NavGeneral, "", "general"));
        nav.MenuItems.Add(NavItem(Strings.Current.NavAppearance, "", "appearance"));
        nav.PaneHeader = new StackPanel
        {
            Margin = new Thickness(12, 22, 12, 22),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = Strings.Current.SettingsTitle, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Theme.Res("BioTapTextBrush") },
                new TextBlock { Text = Strings.Current.SettingsSubtitle, FontSize = 12, Foreground = Theme.Res("BioTapTextMutedBrush") },
            },
        };
        // Real state from the service connection (never a fixed "pronto")
        var status = new Grid { ColumnSpacing = 8, Children = { statusDot, statusText } };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(statusText, 1);
        nav.PaneFooter = new Border
        {
            BorderBrush = Theme.Res("BioTapLineBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(14, 0, 14, 0), Padding = new Thickness(0, 18, 0, 20), Child = status,
        };
        AutomationProperties.SetName(status, Strings.Current.SensorStatusName);
        nav.SelectionChanged += (_, e) => Ui.Run(() => ShowPage((e.SelectedItem as NavigationViewItem)?.Tag as string), "settings page");
        // One layout for the window's lifetime: the banner keeps a single parent; only the page changes
        nav.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Padding = new Thickness(35, 36, 35, 35), Children = { banner, pageHost } } };

        // Title bar drawn by the app, in the dark theme, with the fingerprint mark
        var titleBar = new Grid
        {
            Height = 48, Background = Theme.Res("BioTapTitleBarBrush"), Padding = new Thickness(18, 0, 0, 0),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new FontIcon { Glyph = FingerprintGlyph, FontSize = 18, Foreground = Theme.Res("BioTapAccentLightBrush") },
                        new TextBlock { Text = "BioTap", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Res("BioTapTextBrush") },
                    },
                },
            },
        };
        var root = new Grid { Background = Theme.Res("BioTapContentBrush"), Children = { titleBar, nav } };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(nav, 1);

        window = new Window { Title = Strings.Current.WindowTitle, Content = root, ExtendsContentIntoTitleBar = true };
        window.SetTitleBar(titleBar);
        window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "BioTap.ico")); // taskbar and Alt+Tab
        var bar = window.AppWindow.TitleBar;
        bar.PreferredHeightOption = TitleBarHeightOption.Tall;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = Theme.Color("BioTapTextColor");
        bar.ButtonInactiveForegroundColor = Theme.Color("BioTapTextMutedColor");
        bar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
        bar.ButtonHoverForegroundColor = Theme.Color("BioTapTextColor");
        bar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        var scale = PInvoke.GetDpiForWindow(hwnd) / 96.0;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1040 * scale), (int)(720 * scale)));
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false; // fixed size
            p.IsMaximizable = false;
        }
        window.Closed += (_, _) => Ui.Run(() => Closed?.Invoke(), "settings closed");
        // NavigationView refuses SelectedItem before it is loaded (COMException 0x80070490): select once it is
        nav.Loaded += (_, _) => loaded.TrySetResult();
        RefreshStatus();
    }

    public void Close() => window.Close();

    static NavigationViewItem NavItem(string text, string glyph, string tag)
        => new() { Content = text, Icon = new FontIcon { Glyph = glyph }, Tag = tag };

    readonly TaskCompletionSource loaded = new();

    /// <param name="page">Tag da página a abrir ("general"...) quando a janela é nova; null = a primeira.</param>
    public async void Show(string? page = null)
    {
        try
        {
            window.Activate();
            PInvoke.SetForegroundWindow(hwnd);
            await loaded.Task; // completes after the first Activate puts the content on screen
            if (nav.SelectedItem is null) nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == page) ?? nav.MenuItems[0];
            if (dialogOpen) return; // an editor is already open: just bring the window up
            saved = host.Loaded.Config; // pick up a config reloaded since this window was built
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
            Banner(InfoBarSeverity.Error, Strings.Current.BannerConfigReadOnly, string.Join(" ", loaded.Errors));
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
            Banner(InfoBarSeverity.Error, Strings.Current.BannerNotSaved, outcome.Message);
            return false;
        }
        host.Reload();
        saved = host.Loaded.Config;
        if (outcome.Message is { } warning) Banner(InfoBarSeverity.Warning, Strings.Current.BannerSavedWithWarning, warning);
        else banner.IsOpen = false; // success is shown where the user acted (the list redraws; Salvar says "Salvo")
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

    // ---------- shared page pieces ----------

    static UIElement Heading(string title, string description, UIElement? action = null)
    {
        var text = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = title, FontSize = 27, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = -20, Foreground = Theme.Res("BioTapTextBrush") },
                new TextBlock { Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Res("BioTapTextMutedBrush") },
            },
        };
        var grid = new Grid { ColumnSpacing = 20, Margin = new Thickness(0, 0, 0, 30), Children = { text } };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (action is FrameworkElement a)
        {
            a.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(a, 1);
            grid.Children.Add(a);
        }
        return grid;
    }

    static TextBlock SectionLabel(string text) => new() { Text = text, Style = Theme.Style("BioTapSectionLabelStyle"), Margin = new Thickness(0, 0, 0, 12) };

    /// <summary>A card holding setting rows separated by a thin line.</summary>
    static Border Group(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Rectangle { Height = 1, Fill = Theme.Res("BioTapLineBrush") });
            stack.Children.Add(rows[i]);
        }
        return new Border
        {
            Background = Theme.Res("BioTapCardBrush"), BorderBrush = Theme.Res("BioTapLineBrush"), BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["BioTapCardRadius"], Margin = new Thickness(0, 0, 0, 28), Child = stack,
        };
    }

    /// <summary>Name and description on the left, control on the right; the control drops below when the row is narrow.</summary>
    static Grid SettingRow(string title, string description, FrameworkElement control, out TextBlock descriptionBlock)
    {
        descriptionBlock = new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Foreground = Theme.Res("BioTapTextMutedBrush") };
        var text = new StackPanel
        {
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Res("BioTapTextBrush") }, descriptionBlock },
        };
        AutomationProperties.SetName(control, title);
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { Padding = new Thickness(21), ColumnSpacing = 24, RowSpacing = 12, Children = { text, control } };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.SizeChanged += (_, e) => Ui.Run(() =>
        {
            var narrow = e.NewSize.Width < 480;
            Grid.SetColumn(control, narrow ? 0 : 1);
            Grid.SetRow(control, narrow ? 1 : 0);
            control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }, "setting row size");
        Grid.SetColumn(control, 1);
        return row;
    }

    // Always enabled: a NumberBox commits typed text only when it loses focus, which the click on Salvar causes
    static Button SaveButton() => new() { Content = Strings.Current.Save, Style = Theme.Style("AccentButtonStyle"), MinWidth = 104 };

    /// <summary>Saves through <paramref name="save"/>; on success the button reads "✓ Salvo" for 2 s.</summary>
    async Task SaveFromButton(Button save, AppConfig next)
    {
        save.IsEnabled = false;
        try
        {
            if (!await SaveAsync(next)) return;
            save.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "", FontSize = 12 }, new TextBlock { Text = Strings.Current.Saved } } };
            var back = window.DispatcherQueue.CreateTimer();
            back.Interval = TimeSpan.FromSeconds(2);
            back.IsRepeating = false;
            back.Tick += (_, _) => Ui.Run(() => save.Content = Strings.Current.Save, "save label");
            back.Start();
        }
        finally { save.IsEnabled = true; }
    }

    // ---------- Ações ----------

    UIElement ActionsPage()
    {
        var page = new StackPanel();
        page.Children.Add(Heading(Strings.Current.NavActions, Strings.Current.ActionsDescription, NewActionButton()));
        page.Children.Add(SectionLabel(Strings.Current.YourActions));
        var list = new StackPanel { Spacing = 10 };
        if (saved.Actions.Count == 0)
            list.Children.Add(Group(new TextBlock { Text = PanelModel.EmptyText, Margin = new Thickness(21), Foreground = Theme.Res("BioTapTextMutedBrush") }));
        foreach (var a in saved.Actions.OrderBy(a => a.Taps)) list.Children.Add(ActionCard(a));
        page.Children.Add(list);
        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 24, 0, 0),
            Children =
            {
                new FontIcon { Glyph = FingerprintGlyph, FontSize = 16, Foreground = Theme.Res("BioTapTextMutedBrush") },
                new TextBlock { Text = Strings.Current.TapHint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Res("BioTapTextMutedBrush") },
            },
        });
        return page;
    }

    Button NewActionButton()
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var all = host.Presets.All.ToList();
        var listed = Categories.SelectMany(c => c.Presets).ToHashSet();
        var groups = Categories.Select(c => (Name: c.Name(Strings.Current), Presets: c.Presets.Select(id => all.FirstOrDefault(p => p.Id == id)).OfType<IPreset>().ToList())).ToList();
        groups.Add((Strings.Current.CatOther, all.Where(p => !listed.Contains(p.Id)).ToList()));
        foreach (var (name, presets) in groups.Where(g => g.Presets.Count > 0))
        {
            var sub = new MenuFlyoutSubItem { Text = name };
            foreach (var preset in presets)
            {
                var item = new MenuFlyoutItem { Text = preset.DisplayName };
                if (preset.DefaultIcon.Kind == "glyph") item.Icon = new FontIcon { Glyph = preset.DefaultIcon.GlyphText() };
                item.Click += (_, _) => Ui.RunAsync(() => EditAsync(ActionEditor.NewAction(saved, preset), isNew: true), "new action");
                sub.Items.Add(item);
            }
            menu.Items.Add(sub);
        }
        return new Button
        {
            Style = Theme.Style("AccentButtonStyle"),
            Padding = new Thickness(15, 9, 15, 9),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 12 }, new TextBlock { Text = Strings.Current.NewAction, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } } },
            Flyout = menu,
        };
    }

    UIElement ActionCard(ActionConfig a)
    {
        // Spec §5: an action the loader disabled as invalid says so (otherwise it just looks switched off)
        var invalid = host.Loaded.InvalidActions.Contains(a.Id);
        var presetName = host.Presets.Get(a.Preset)?.DisplayName ?? a.Preset;
        var detail = PresetEditors.Summary(a.Preset, a.Params);
        var summary = invalid ? Strings.Current.InvalidOpenToFix : detail.Length > 0 ? $"{presetName} · {detail}" : presetName;
        if (!a.Enabled && !invalid) summary += " · " + Strings.Current.Disabled;

        // Left region: tap count | icon | name and summary. Clicking it opens the editor.
        var taps = new Border
        {
            Width = 66, Padding = new Thickness(0, 0, 17, 0), BorderBrush = Theme.Res("BioTapDividerBrush"), BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = a.Taps.ToString(), FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Theme.Res("BioTapTextBrush") },
                    new TextBlock { Text = a.Taps == 1 ? Strings.Current.TapSingular : Strings.Current.TapPlural, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Theme.Res("BioTapTextMutedBrush") },
                },
            },
        };
        var icon = invalid ? new FontIcon { Glyph = Glyphs.Warning, FontSize = 22 } : Icons.Element(a.Icon, 23);
        if (icon is FontIcon fi) fi.Foreground = Theme.Res(invalid ? "BioTapErrorBrush" : "BioTapAccentLightBrush");
        var tile = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = Theme.Res("BioTapTileBrush"),
            BorderBrush = Theme.Res("BioTapTileBorderBrush"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = icon,
        };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var details = new StackPanel
        {
            Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = a.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.Res("BioTapTextBrush") },
                new TextBlock { Text = summary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.Res("BioTapTextMutedBrush") },
            },
        };
        var mainGrid = new Grid { ColumnSpacing = 19, Children = { taps, tile, details } };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tile, 1);
        Grid.SetColumn(details, 2);
        var main = new Button { Style = Theme.Style("BioTapPlainButtonStyle"), Padding = new Thickness(18, 21, 0, 21), Content = mainGrid };
        AutomationProperties.SetName(main, Strings.Current.EditNamed(a.Name, Theme.Taps(a.Taps)));
        main.Click += (_, _) => Ui.RunAsync(() => EditAsync(a, isNew: false), "edit action");

        // Right region: test, on/off, options. Separate buttons, so using them never opens the editor.
        var test = GhostButton("", Strings.Current.Test, Strings.Current.TestNamed(a.Name));
        test.Click += (_, _) => Ui.Run(() => host.Router.Test(a), "test action");

        var enabled = new ToggleSwitch { IsOn = a.Enabled, OnContent = null, OffContent = null, MinWidth = 0, Width = 44, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(enabled, Strings.Current.EnableNamed(a.Name));
        // Turning an action on or off saves at once (a single switch has nothing else to confirm)
        enabled.Toggled += (_, _) => Ui.RunAsync(async () =>
        {
            await SaveAsync(ActionEditor.WithAction(saved, a with { Enabled = enabled.IsOn }));
            ShowPage("actions"); // redraw with the real state (saved or unchanged)
        }, "toggle action");

        var more = GhostButton("", Strings.Current.ActionOptions, Strings.Current.OptionsOf(a.Name));
        var edit = new MenuFlyoutItem { Text = Strings.Current.Edit, Icon = new FontIcon { Glyph = "" } };
        edit.Click += (_, _) => Ui.RunAsync(() => EditAsync(a, isNew: false), "edit action");
        var remove = new MenuFlyoutItem { Text = Strings.Current.Delete, Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) => Ui.RunAsync(() => DeleteAsync(a), "delete action");
        more.Flyout = new MenuFlyout { Items = { edit, remove } };

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13, Margin = new Thickness(13, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, Children = { test, enabled, more } };

        var grid = new Grid { Children = { main, tools } };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(tools, 1);
        var card = new Border
        {
            Background = Theme.Res("BioTapCardBrush"), BorderBrush = Theme.Res("BioTapLineBrush"), BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["BioTapCardRadius"], Child = grid,
            BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(150) },
        };
        // A switched-off action dims its count and icon; the name and summary keep full contrast and say "Desativada"
        if (!a.Enabled) taps.Opacity = tile.Opacity = 0.5;
        card.PointerEntered += (_, _) => { card.Background = Theme.Res("BioTapCardHoverBrush"); card.BorderBrush = Theme.Res("BioTapCardHoverBorderBrush"); };
        void Rest() { card.Background = Theme.Res("BioTapCardBrush"); card.BorderBrush = Theme.Res("BioTapLineBrush"); }
        card.PointerExited += (_, _) => Rest();
        card.PointerCanceled += (_, _) => Rest();
        card.PointerCaptureLost += (_, _) => Rest();
        return card;
    }

    static Button GhostButton(string glyph, string tooltip, string name)
    {
        var b = new Button { Style = Theme.Style("BioTapGhostButtonStyle"), Width = 30, Height = 32, Content = new FontIcon { Glyph = glyph, FontSize = 14 } };
        ToolTipService.SetToolTip(b, tooltip);
        AutomationProperties.SetName(b, name);
        return b;
    }

    async Task DeleteAsync(ActionConfig a)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot, Title = Strings.Current.DeleteTitle(a.Name), Content = Strings.Current.DeleteBody(Theme.Taps(a.Taps)),
            PrimaryButtonText = Strings.Current.Delete, CloseButtonText = Strings.Current.Cancel, DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(confirm) == ContentDialogResult.Primary && await SaveAsync(ActionEditor.Without(saved, a.Id)))
        {
            IconCache.Remove(Program.DataFolder, a.Id);
            ShowPage("actions");
        }
    }

    async Task EditAsync(ActionConfig action, bool isNew)
    {
        var taps = new NumberBox
        {
            Header = Strings.Current.TapsHeader, Description = Strings.Current.TapsDescription,
            Minimum = 1, Maximum = Protocol.MaxTaps, Value = action.Taps, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 1,
        };
        var name = new TextBox
        {
            Header = Strings.Current.ActionName, Text = action.Name, MaxLength = 80,
            Description = Strings.Current.ActionNameDescription,
        };
        var icon = new ComboBox { Header = Strings.Current.IconHeader, MinWidth = 220 };
        // Only "Abrir" can show the program's own icon; it is the default for new actions
        var hasProgramIcon = action.Preset == "openTarget";
        if (hasProgramIcon)
            icon.Items.Add(new ComboBoxItem { Tag = "exe", Content = new TextBlock { Text = Strings.Current.ProgramIcon } });
        foreach (var (glyph, label) in IconChoices)
            icon.Items.Add(new ComboBoxItem { Tag = glyph, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = Glyphs.For(new(OutcomeKind.Ran, "", null, new IconRef("glyph", glyph))) }, new TextBlock { Text = label(Strings.Current) } } } });
        // A glyph outside the list (set in config.json) stays as it is unless the user picks another
        if (action.Icon.Kind == "exe" ? !hasProgramIcon : !IconChoices.Any(c => c.Glyph == action.Icon.Value))
            icon.Items.Add(new ComboBoxItem { Tag = action.Icon.Kind == "exe" ? "exe" : action.Icon.Value, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Element(action.Icon, 16), new TextBlock { Text = Strings.Current.CurrentIcon } } } });
        var wantProgramIcon = hasProgramIcon && (isNew || action.Icon.Kind == "exe");
        var selectedTag = wantProgramIcon || action.Icon.Kind == "exe" ? "exe" : action.Icon.Value;
        icon.SelectedItem = icon.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selectedTag) ?? icon.Items[0];
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        ContentDialog? dialogRef = null;
        UIElement? mainContent = null;
        // A sequence step editor takes over the one dialog; Salvar/Testar stay off so half-edited state is never saved
        var stepShowing = false;
        void Swap(UIElement? step)
        {
            stepShowing = step is not null;
            dialogRef!.Content = step ?? mainContent;
            dialogRef.IsPrimaryButtonEnabled = dialogRef.IsSecondaryButtonEnabled = step is null;
        }
        var fields = PresetEditors.Build(action.Preset, action.Params, window.AppWindow.Id, host.Presets, Swap);
        var presetName = host.Presets.Get(action.Preset)?.DisplayName ?? action.Preset;
        UIElement parameters = fields.Panel is Microsoft.UI.Xaml.Controls.Panel { Children.Count: 0 }
            ? new TextBlock { Text = Strings.Current.NoParams, FontSize = 12, Foreground = Theme.Res("BioTapTextMutedBrush") }
            : fields.Panel;

        var dialog = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot,
            Title = isNew ? Strings.Current.NewAction : Strings.Current.EditAction,
            PrimaryButtonText = Strings.Current.Save,
            SecondaryButtonText = Strings.Current.Test,
            CloseButtonText = Strings.Current.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 12, MinWidth = 460,
                    Children =
                    {
                        error,
                        new TextBlock { Text = presetName, FontSize = 13, Foreground = Theme.Res("BioTapAccentLightBrush") },
                        SectionLabel(Strings.Current.SectionIdentification), taps, name, icon,
                        new Rectangle { Height = 1, Fill = Theme.Res("BioTapLineBrush"), Margin = new Thickness(0, 6, 0, 6) },
                        SectionLabel(Strings.Current.SectionParameters), parameters,
                    },
                },
            },
        };

        dialogRef = dialog;
        // Esc / Cancelar inside a step editor goes back to the sequence (dropping only that step's unsaved edits) instead of closing the dialog
        dialog.Closing += (_, e) => Ui.Run(() =>
        {
            if (stepShowing && e.Result == ContentDialogResult.None) { e.Cancel = true; Swap(null); }
        }, "editor closing");
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
        if (edited.Icon.Kind == "exe" && hasProgramIcon)
        {
            var target = edited.Params["target"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
            var png = await Task.Run(() => new IconExtractor().ExtractPng(target, 48)); // reads the disk
            // An action that already has a cached icon keeps it only when this target is extractable (so the failure was transient);
            // a new target that can never give an icon (URL, .txt…) falls back to the glyph and the cached file is removed below
            var keepOld = png is null && action.Icon.Kind == "exe" && IconCache.CanExtractFrom(Environment.ExpandEnvironmentVariables(target).Replace('/', '\\'));
            if (!keepOld)
                edited = edited with { Icon = IconCache.Store(Program.DataFolder, edited.Id, png, host.Presets.Get(edited.Preset)?.DefaultIcon ?? new IconRef("glyph", "E8E5")) };
        }
        if (!await SaveAsync(ActionEditor.WithAction(saved, edited))) return;
        if (edited.Icon.Kind != "exe") IconCache.Remove(Program.DataFolder, edited.Id); // glyph now, or no icon could be read
        ShowPage("actions");
    }

    // ---------- Geral ----------

    UIElement GeneralPage()
    {
        var startup = new StartupTask();
        var startWithWindows = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, Width = 44 };
        var startRow = SettingRow(Strings.Current.StartWithWindows, Strings.Current.StartWithWindowsDescription, startWithWindows, out var startDescription);
        try { startWithWindows.IsOn = startup.IsEnabled(); }
        catch (Exception)
        {
            startWithWindows.IsEnabled = false;
            startDescription.Text = Strings.Current.TaskSchedulerUnreadable;
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
                Banner(InfoBarSeverity.Error, Strings.Current.StartupChangeFailed,
                    e is InvalidOperationException ? e.Message : null);
                reverting = true;
                startWithWindows.IsOn = !startWithWindows.IsOn; // show the real state
                reverting = false;
            }
        }, "startup toggle");

        var timeout = new NumberBox { Minimum = TapCounter.MinTimeoutMs, Maximum = TapCounter.MaxTimeoutMs, Value = saved.TapTimeoutMs, SmallChange = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 180 };
        var timeoutRow = SettingRow(Strings.Current.TapInterval,
            Strings.Current.TapIntervalDescription(TapCounter.MinTimeoutMs, TapCounter.MaxTimeoutMs),
            timeout, out _);
        // Spec da tradução §4: salvo pelo botão da página, como os outros campos de Geral
        var language = new ComboBox { MinWidth = 180 };
        language.Items.Add(new ComboBoxItem { Content = Strings.Current.LanguagePtBr, Tag = Language.PtBr });
        language.Items.Add(new ComboBoxItem { Content = Strings.Current.LanguageEn, Tag = Language.En });
        language.SelectedItem = language.Items.Cast<ComboBoxItem>().First(i => (Language)i.Tag == Strings.Current.Language);
        var languageRow = SettingRow(Strings.Current.LanguageLabel, Strings.Current.LanguageDescription, language, out _);

        var save = SaveButton();
        int Read() => double.IsNaN(timeout.Value) ? saved.TapTimeoutMs : (int)Math.Round(timeout.Value);
        // Sem mudança no seletor, o config.json continua sem idioma (o do instalador segue valendo numa reinstalação)
        string? ReadLanguage() => (Language)((ComboBoxItem)language.SelectedItem).Tag is var l && l == Strings.Current.Language ? saved.Language : LanguageChoice.Code(l);
        save.Click += (_, _) => Ui.RunAsync(() => SaveFromButton(save, saved with { TapTimeoutMs = Read(), Language = ReadLanguage() }), "save general");

        return new StackPanel
        {
            Children =
            {
                Heading(Strings.Current.NavGeneral, Strings.Current.GeneralDescription),
                SectionLabel(Strings.Current.SectionStartup),
                Group(startRow),
                SectionLabel(Strings.Current.SectionTapDetection),
                Group(timeoutRow),
                SectionLabel(Strings.Current.SectionLanguage),
                Group(languageRow),
                save,
            },
        };
    }

    /// <summary>Called by AppHost on the UI thread whenever the service or sensor state changes.</summary>
    public void RefreshStatus()
    {
        var text = host.StatusText["BioTap: ".Length..];
        statusText.Text = host.SensorReady ? Strings.Current.SensorReady : char.ToUpper(text[0]) + text[1..];
        statusText.Foreground = Theme.Res(host.StatusProblem ? "BioTapErrorBrush" : "BioTapTextBrush");
        statusDot.Fill = Theme.Res(host.SensorReady ? "BioTapAccentLightBrush" : host.StatusProblem ? "BioTapErrorBrush" : "BioTapTextMutedBrush");
    }

    static readonly string InstalledExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BioTap", "App", "BioTap.exe");

    // ---------- Aparência e feedback ----------

    UIElement AppearancePage()
    {
        // The real pill content on the same tint; only the blur of what is behind cannot be shown inside the window
        var sample = PanelModel.Items(saved).FirstOrDefault();
        var outcome = new ActionOutcome(OutcomeKind.Ran, sample?.Name ?? Strings.Current.ActionName, null, sample?.Icon ?? new IconRef("glyph", "E928"));
        var preview = new Border
        {
            Height = 160, CornerRadius = (CornerRadius)Application.Current.Resources["BioTapCardRadius"], Background = Theme.Res("BioTapTitleBarBrush"),
            BorderBrush = Theme.Res("BioTapLineBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 25),
            Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8), // the radius Windows gives the real window
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb((byte)(255 * Theme.GlassTintOpacity), Theme.GlassTint.R, Theme.GlassTint.G, Theme.GlassTint.B)),
                BorderBrush = new SolidColorBrush(Theme.GlassBorder), BorderThickness = new Thickness(1),
                Child = OverlayWindow.Content(outcome),
            },
        };
        AutomationProperties.SetName(preview, Strings.Current.PreviewNamed(outcome.Title));

        var show = new ToggleSwitch { IsOn = saved.Overlay.Enabled, OnContent = null, OffContent = null, MinWidth = 0, Width = 44 };
        var duration = new NumberBox { Minimum = OverlayConfig.MinDurationMs, Maximum = OverlayConfig.MaxDurationMs, Value = saved.Overlay.DurationMs, SmallChange = 250, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 180 };
        var save = SaveButton();
        OverlayConfig Read() => new(show.IsOn, double.IsNaN(duration.Value) ? saved.Overlay.DurationMs : (int)Math.Round(duration.Value));
        save.Click += (_, _) => Ui.RunAsync(() => SaveFromButton(save, saved with { Overlay = Read() }), "save appearance");

        return new StackPanel
        {
            Children =
            {
                Heading(Strings.Current.NavAppearance, Strings.Current.AppearanceDescription),
                SectionLabel(Strings.Current.SectionPreview),
                preview,
                Group(
                    SettingRow(Strings.Current.ShowConfirmation, Strings.Current.ShowConfirmationDescription, show, out _),
                    SettingRow(Strings.Current.TimeOnScreen, Strings.Current.TimeOnScreenDescription(OverlayConfig.MinDurationMs, OverlayConfig.MaxDurationMs), duration, out _)),
                save,
            },
        };
    }
}
