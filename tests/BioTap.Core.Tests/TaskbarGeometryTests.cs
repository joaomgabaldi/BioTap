namespace BioTap.Core.Tests;

public class TaskbarGeometryTests
{
    static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void BottomTaskbar_CenteredAboveIt()
        => Assert.Equal((860, 1080 - 48 - 8 - 100), TaskbarGeometry.Place(new(0, 0, 1920, 1032), Monitor, 200, 100));

    [Fact]
    public void TopTaskbar_CenteredBelowIt()
        => Assert.Equal((860, 48 + 8), TaskbarGeometry.Place(new(0, 48, 1920, 1032), Monitor, 200, 100));

    [Fact]
    public void LeftTaskbar_BottomCenterOfWorkArea()
        => Assert.Equal((60 + (1860 - 200) / 2, 1080 - 8 - 100), TaskbarGeometry.Place(new(60, 0, 1860, 1080), Monitor, 200, 100));

    [Fact]
    public void HiddenTaskbar_LeavesRoomForTheAutoHideStrip()
        => Assert.Equal((860, 1080 - 48 - 8 - 100), TaskbarGeometry.Place(Monitor, Monitor, 200, 100));

    [Fact]
    public void SecondMonitor_UsesItsOwnCoordinates()
        => Assert.Equal((1920 + 1180, 1440 - 48 - 8 - 100), TaskbarGeometry.Place(new(1920, 0, 2560, 1392), new(1920, 0, 2560, 1440), 200, 100));

    [Fact]
    public void ContentWiderThanWorkArea_ClampedInside()
    {
        var (x, _) = TaskbarGeometry.Place(new(0, 0, 800, 600), new(0, 0, 800, 648), 1000, 100);
        Assert.Equal(0, x);
    }
}
