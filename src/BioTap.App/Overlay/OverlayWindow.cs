using BioTap.App.Shell;
using BioTap.Core.Actions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BioTap.App.Overlay;

/// <summary>
/// Confirmation pill (approved mockup docs/mockups/confirmacao.html): the action's icon and name on the same glass
/// as the panel, above the taskbar or above the open panel. Never takes focus or clicks: the action may target the
/// window the user is in (paste, always-on-top, minimize others). One pill: a new outcome updates it in place.
/// </summary>
sealed class OverlayWindow
{
    public const double MaxWidth = 360;
    readonly GlassWindow glass;
    readonly Border host = new();
    readonly DispatcherQueueTimer hideTimer;

    public OverlayWindow()
    {
        // TRANSPARENT passes clicks through; NOACTIVATE keeps the focus where the user is typing
        glass = new GlassWindow(host, WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOPMOST);
        // Keep the pill out of screenshots (the action runs after the overlay is shown); fails on Windows older than 10 2004, where it may show up
        _ = PInvoke.SetWindowDisplayAffinity(glass.Hwnd, WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE);

        hideTimer = glass.Window.DispatcherQueue.CreateTimer();
        hideTimer.IsRepeating = false;
        hideTimer.Tick += (_, _) => Ui.Run(() => glass.Hide(), "overlay hide");
    }

    /// <summary>
    /// Shows <paramref name="o"/>, or updates the pill already on screen (no new entrance; a running exit turns
    /// back) and restarts its time. <paramref name="panel"/> is the open panel's bounds, to stay above it. Call on
    /// the UI thread.
    /// </summary>
    public void Show(ActionOutcome o, TimeSpan duration, RectInt32? panel)
    {
        var content = Content(o);
        // Pinned to the top-left: in the one frame where the window already has its new size but XAML still draws the
        // previous layout, the content stays where it belongs instead of shifting by half the change (centered text got cut)
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var area = panel is { } p ? DisplayArea.GetFromRect(p, DisplayAreaFallback.Primary) : Placement.UnderCursor();
        var scale = glass.ScaleOn(area);
        content.Measure(new Windows.Foundation.Size(MaxWidth, double.PositiveInfinity));
        var size = GlassWindow.SizeFor(content.DesiredSize, scale);
        RectInt32 Place(SizeInt32 s) => panel is { } anchor ? Placement.Above(area, anchor, s) : Placement.AboveTaskbar(area, s);

        var token = ++swap;
        if (glass.IsOnScreen)
        {
            // The window resizes at once but the new text only appears with the next XAML frame. So: first a size that
            // fits both texts, then the swap, then the final size once the new text has been drawn.
            var both = new SizeInt32(Math.Max(size.Width, glass.Size.Width), Math.Max(size.Height, glass.Size.Height));
            glass.Show(Place(both), scale, activate: false);
            host.Child = content;
            Ui.AfterFrames(2, () => { if (token == swap && glass.IsShown) glass.Show(Place(size), scale, activate: false); });
        }
        else
        {
            host.Child = content;
            glass.Show(Place(size), scale, activate: false);
        }

        hideTimer.Stop();
        hideTimer.Interval = duration;
        hideTimer.Start();
    }

    int swap;

    /// <summary>
    /// The pill's content: icon and name; errors keep their glyph and add the safe detail line. Also drawn as the
    /// preview in the preferences.
    /// </summary>
    public static FrameworkElement Content(ActionOutcome o)
    {
        FrameworkElement icon = o.Kind is OutcomeKind.Ran && o.Icon is { } own
            ? Icons.Element(own, 23)
            : new FontIcon { Glyph = Glyphs.For(o), FontSize = 23 };
        if (icon is FontIcon f)
            f.Foreground = o.Kind switch
            {
                OutcomeKind.Ran => Theme.Res("BioTapAccentLightBrush"),
                OutcomeKind.Failed => Theme.Res("BioTapErrorBrush"),
                _ => Theme.Res("BioTapTextMutedBrush"),
            };
        icon.VerticalAlignment = VerticalAlignment.Center;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = o.Title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = Theme.Res("BioTapTextBrush"),
            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (o.Detail is { } detail)
            text.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 12, Foreground = Theme.Res("BioTapTextMutedBrush"),
                TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
            });

        var grid = new Grid { Padding = new Thickness(17, 14, 17, 14), MinHeight = 52, ColumnSpacing = 12, MaxWidth = MaxWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }
}
