using BioTap.App.Shell;
using BioTap.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.System;
using BioTap.Core.Text;

namespace BioTap.App.Panel;

/// <summary>
/// The action panel (approved mockup docs/mockups/painel.html): one glass surface with a row of actions (icon,
/// tap count below), a divider and a smaller gear; centered 8 px above the taskbar. Clicking an action runs it;
/// the gear opens the preferences. Closes on Esc or when it loses focus.
/// </summary>
sealed class PanelWindow
{
    readonly GlassWindow glass;
    readonly Grid root;
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
    long lastHideTick;
    int shows;

    public event Action<Guid>? ItemClicked;
    public event Action? GearClicked;

    /// <summary>Where the panel is while shown (the confirmation goes above it).</summary>
    public RectInt32? Bounds => glass.Bounds;

    public PanelWindow()
    {
        root = new Grid { Padding = new Thickness(9), Children = { row } };
        root.KeyDown += (_, e) => Ui.Run(() => { if (e.Key == VirtualKey.Escape) Hide(); }, "panel key");
        glass = new GlassWindow(root);
        glass.Window.Activated += (_, e) => Ui.Run(() => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(); }, "panel activated");
    }

    public void Toggle(IReadOnlyList<PanelItem> items)
    {
        // Clicking the tray icon first deactivates (and so hides) the panel; that same click must not reopen it
        if (glass.IsShown || Environment.TickCount64 - lastHideTick < 300) Hide();
        else Show(items);
    }

    public void Show(IReadOnlyList<PanelItem> items)
    {
        shows++;
        row.Children.Clear();
        if (items.Count == 0)
            row.Children.Add(new TextBlock { Text = PanelModel.EmptyText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0), Foreground = Theme.Res("BioTapTextMutedBrush") });
        foreach (var item in items) row.Children.Add(Tile(item));
        row.Children.Add(new Rectangle { Width = 1, Height = 35, Fill = Theme.Res("BioTapDividerBrush"), Margin = new Thickness(5, 0, 5, 0) });
        var gear = new Button
        {
            Style = Theme.Style("BioTapGhostButtonStyle"),
            Content = new FontIcon { Glyph = "", FontSize = 18 },
            Width = 36,
            Height = 44,
            CornerRadius = new CornerRadius(7),
        };
        ToolTipService.SetToolTip(gear, Strings.Current.SettingsTitle);
        AutomationProperties.SetName(gear, Strings.Current.SettingsTitle);
        gear.Click += (_, _) => Ui.Run(() => { Hide(); GearClicked?.Invoke(); }, "panel gear");
        row.Children.Add(gear);

        var area = Placement.UnderCursor();
        var scale = glass.ScaleOn(area);
        root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        root.IsHitTestVisible = true;
        glass.Show(Placement.AboveTaskbar(area, GlassWindow.SizeFor(root.DesiredSize, scale)), scale, activate: true); // focus, so Esc and click-outside close it
    }

    FrameworkElement Tile(PanelItem item)
    {
        var label = $"{item.Name} · {Theme.Taps(item.Taps)}";
        var button = new Button
        {
            Style = Theme.Style("BioTapPanelTileStyle"),
            Content = new StackPanel
            {
                Spacing = 11,
                Children =
                {
                    Icons.Element(item.Icon, 27),
                    new TextBlock { Text = item.Taps.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        // Runs once the panel is gone, so window presets act on the window that was in front (as a tap would)
        button.Click += (_, _) => Ui.Run(() => Hide(() => ItemClicked?.Invoke(item.Id)), "panel item");
        return button;
    }

    /// <param name="done">Called after the exit (not if the panel is reopened meanwhile).</param>
    public void Hide(Action? done = null)
    {
        if (!glass.IsShown) return;
        lastHideTick = Environment.TickCount64;
        root.IsHitTestVisible = false; // no second click while it leaves
        // Reopened during the exit: GlassWindow still calls done, but a clicked action would then act on the panel itself
        var shown = shows;
        glass.Hide(done is null ? null : () => Ui.Run(() => { if (shown == shows) done(); }, "panel hidden"));
    }
}
