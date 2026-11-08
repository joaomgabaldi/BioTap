using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BioTap.App.Shell;

/// <summary>
/// Acrylic that always renders as "active". The overlay is never activated (it must not take focus), and the stock
/// DesktopAcrylicBackdrop would fall back to the flat inactive color.
/// </summary>
sealed class ActiveAcrylicBackdrop : SystemBackdrop
{
    DesktopAcrylicController? controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        controller = new DesktopAcrylicController();
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
