namespace BioTap.Core.Tests;

public class RevealTests
{
    const int Enter = 180, Exit = 150;

    [Fact]
    public void Show_GoesFromHiddenToShownInTheEnterTime()
    {
        var r = new Reveal();
        r.Start(show: true, nowMs: 1000, Enter, Exit);
        Assert.Equal(0, r.Tick(1000));
        var mid = r.Tick(1090);
        Assert.InRange(mid, 0.5, 0.99); // decelerates: past half at half time
        Assert.True(r.Running);
        Assert.Equal(1, r.Tick(1180));
        Assert.False(r.Running);
        Assert.True(r.Shown);
    }

    [Fact]
    public void Hide_AcceleratesAndEndsHidden()
    {
        var r = new Reveal();
        r.Start(true, 0, Enter, Exit);
        r.Tick(Enter);
        r.Start(false, 1000, Enter, Exit);
        Assert.InRange(r.Tick(1075), 0.5, 0.99); // accelerates: less than half gone at half time
        Assert.Equal(0, r.Tick(1150));
        Assert.False(r.Running);
        Assert.False(r.Shown);
    }

    [Fact]
    public void ShowDuringHide_ContinuesFromTheCurrentValueWithoutJumping()
    {
        var r = new Reveal();
        r.Start(true, 0, Enter, Exit);
        r.Tick(Enter);
        r.Start(false, 1000, Enter, Exit);
        var at = r.Tick(1100);
        r.Start(true, 1100, Enter, Exit);
        Assert.Equal(at, r.Tick(1100), 6);
        Assert.True(r.Tick(1110) > at);
        // Only the remaining distance is animated, so it takes a fraction of the full time
        Assert.Equal(1, r.Tick(1100 + (int)Math.Ceiling(Enter * (1 - at))));
    }

    [Fact]
    public void HideDuringShow_ReversesFromTheCurrentValue()
    {
        var r = new Reveal();
        r.Start(true, 0, Enter, Exit);
        var at = r.Tick(60);
        r.Start(false, 60, Enter, Exit);
        Assert.Equal(at, r.Tick(60), 6);
        Assert.True(r.Tick(70) < at);
        Assert.Equal(0, r.Tick(60 + (int)Math.Ceiling(Exit * at)));
    }

    [Fact]
    public void StartTowardsTheCurrentState_DoesNothing()
    {
        var r = new Reveal();
        r.Start(true, 0, Enter, Exit);
        r.Tick(Enter);
        r.Start(true, 500, Enter, Exit);
        Assert.False(r.Running);
        Assert.Equal(1, r.Tick(500));
    }

    [Fact]
    public void Jump_SetsTheEndStateAtOnce()
    {
        var r = new Reveal();
        r.Start(true, 0, Enter, Exit);
        r.Jump(show: false);
        Assert.False(r.Running);
        Assert.Equal(0, r.Value);
        Assert.False(r.Shown);
    }
}
