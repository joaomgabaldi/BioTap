using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BioTap.App.Shell;

/// <summary>
/// The glass of the panel and the confirmation: system acrylic (real blur of what is behind) tinted with
/// <see cref="Theme.GlassTint"/>, always rendered as "active". The overlay is never activated (it must not take
/// focus), and the stock DesktopAcrylicBackdrop would fall back to the flat inactive color.
/// </summary>
sealed class ActiveAcrylicBackdrop : SystemBackdrop
{
    DesktopAcrylicController? controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = Theme.GlassTint,
            TintOpacity = Theme.GlassTintOpacity,
            LuminosityOpacity = Theme.GlassTintOpacity,
            // Shown when Windows turns transparency off (setting, battery saver): plain #262626
            FallbackColor = Theme.GlassTint,
        };
        controller.AddSystemBackdropTarget(target);
        controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark });
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
    }
}
