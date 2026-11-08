using BioTap.App.Overlay;
using BioTap.App.Shell;
using BioTap.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace BioTap.App.Panel;

/// <summary>
/// The action panel (spec §4, approved mockup): a row of 56 px squares with the tap count below, a separator and a
/// small gear; centered above the taskbar; closes on Esc or when it loses focus.
/// </summary>
sealed class PanelWindow
{
    const int Square = 56, Gap = 8, Pad = 12, CountHeight = 18, GearWidth = 36;
    readonly Window window;
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = Gap, VerticalAlignment = VerticalAlignment.Top };
    readonly HWND hwnd;
    bool visible;

    public event Action<Guid>? ItemClicked;
    public event Action? GearClicked;

    public PanelWindow()
    {
        var root = new Grid { Padding = new Thickness(Pad), Children = { row } };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) Hide(); };
        window = new Window { SystemBackdrop = new BioTap.App.Shell.ActiveAcrylicBackdrop(), Content = root };
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        window.AppWindow.IsShownInSwitchers = false;
        window.Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(); };
        hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    long lastHideTick;

    public void Toggle(IReadOnlyList<PanelItem> items)
    {
        // Clicking the tray icon first deactivates (and so hides) the panel; that same click must not reopen it
        if (visible || Environment.TickCount64 - lastHideTick < 300) Hide();
        else Show(items);
    }

    public void Show(IReadOnlyList<PanelItem> items)
    {
        row.Children.Clear();
        if (items.Count == 0)
            row.Children.Add(new TextBlock { Text = PanelModel.EmptyText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 18, 8, 0), Opacity = 0.8 });
        foreach (var item in items) row.Children.Add(Tile(item));
        row.Children.Add(new Rectangle { Width = 1, Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(2, 4, 2, 4) });
        var gear = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 16 },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Width = GearWidth,
            Height = Square,
            Opacity = 0.7,
        };
        ToolTipService.SetToolTip(gear, "Preferências");
        gear.Click += (_, _) => { Hide(); GearClicked?.Invoke(); };
        row.Children.Add(gear);

        var content = items.Count == 0 ? 190 : items.Count * Square + (items.Count - 1) * Gap;
        Placement.AboveTaskbar(window.AppWindow, hwnd, scale =>
            ((int)((Pad * 2 + content + Gap + 5 + Gap + GearWidth) * scale), (int)((Pad * 2 + Square + 6 + CountHeight) * scale)));
        window.Activate(); // takes focus so Esc and click-outside close it
        // From a second BioTap.exe the foreground lock can refuse Activate; without focus the panel could not close
        PInvoke.SetForegroundWindow(hwnd);
        visible = true;
    }

    FrameworkElement Tile(PanelItem item)
    {
        var button = new Button
        {
            Width = Square,
            Height = Square,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = Glyphs.For(new(Core.Actions.OutcomeKind.Ran, "", null, item.Icon)), FontSize = 28 },
        };
        ToolTipService.SetToolTip(button, item.Name);
        button.Click += (_, _) => { Hide(); ItemClicked?.Invoke(item.Id); };
        return new StackPanel
        {
            Spacing = 6,
            Children = { button, new TextBlock { Text = item.Taps.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Opacity = 0.75 } },
        };
    }

    public void Hide()
    {
        if (!visible) return;
        visible = false;
        lastHideTick = Environment.TickCount64;
        window.AppWindow.Hide();
    }
}
