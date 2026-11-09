namespace BioTap.Core;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Where the panel and the overlay go: centered, <c>gap</c> px from the taskbar (spec §4).</summary>
public static class TaskbarGeometry
{
    const int AutoHideStrip = 48; // an auto-hidden taskbar pops up over the bottom of the screen

    public static (int X, int Y) Place(ScreenRect work, ScreenRect monitor, int width, int height, int gap = 8)
    {
        var x = work.X + (work.Width - width) / 2;
        int y;
        if (work.Y > monitor.Y) y = work.Y + gap;                            // taskbar on top
        else if (work.Bottom < monitor.Bottom) y = work.Bottom - gap - height; // taskbar at the bottom
        else if (work == monitor) y = monitor.Bottom - AutoHideStrip - gap - height; // hidden taskbar
        else y = work.Bottom - gap - height;                                 // taskbar on a side

        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
        return (x, y);
    }

    /// <summary>Centered <c>gap</c> px above <paramref name="anchor"/> (the open panel); below it when the top has no room.</summary>
    public static (int X, int Y) PlaceAbove(ScreenRect work, ScreenRect anchor, int width, int height, int gap = 8)
    {
        var x = anchor.X + (anchor.Width - width) / 2;
        var y = anchor.Y - gap - height;
        if (y < work.Y) y = anchor.Bottom + gap;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
        return (x, y);
    }
}
