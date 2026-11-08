using Microsoft.Extensions.Time.Testing;

namespace BioTap.Core.Tests;

public class FocusPolicyTests
{
    readonly FakeTimeProvider time = new();
    FocusPolicy NewPolicy() => new(time, TimeSpan.FromSeconds(1));
    void Advance(int ms) => time.Advance(TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void Start_NotHello_Acquires()
    {
        var p = NewPolicy();
        Assert.Equal(FocusAction.Acquire, p.Decide(false));
        Assert.True(p.HoldingFocus);
    }

    [Fact]
    public void Start_HelloActive_DoesNotAcquire()
    {
        var p = NewPolicy();
        Assert.Equal(FocusAction.None, p.Decide(true));
        Assert.False(p.HoldingFocus);
    }

    [Fact]
    public void HelloAppears_Releases()
    {
        var p = NewPolicy();
        p.Decide(false);
        Assert.Equal(FocusAction.Release, p.Decide(true));
        Assert.False(p.HoldingFocus);
    }

    [Fact]
    public void HelloGone_WaitsDelayThenAcquires()
    {
        var p = NewPolicy();
        p.Decide(false);
        p.Decide(true);
        Assert.Equal(FocusAction.None, p.Decide(false));
        Advance(999);
        Assert.Equal(FocusAction.None, p.Decide(false));
        Advance(1);
        Assert.Equal(FocusAction.Acquire, p.Decide(false));
        Assert.True(p.HoldingFocus);
    }

    [Fact]
    public void Decide_FlappingWithinHold_StaysReleased()
    {
        var p = NewPolicy();
        p.Decide(false);
        p.Decide(true);
        for (var i = 0; i < 12; i++)
        {
            Advance(250);
            Assert.Equal(FocusAction.None, p.Decide(i % 2 == 0 ? false : true));
        }
        Assert.False(p.HoldingFocus);
    }

    [Fact]
    public void Steady_ReturnsNone()
    {
        var p = NewPolicy();
        p.Decide(false);
        for (var i = 0; i < 5; i++) { Advance(250); Assert.Equal(FocusAction.None, p.Decide(false)); }
        p.Decide(true);
        for (var i = 0; i < 5; i++) { Advance(250); Assert.Equal(FocusAction.None, p.Decide(true)); }
    }

    [Fact]
    public void HelloProcesses_AreTheSpecList()
        => Assert.Equal(["LogonUI", "consent", "CredentialUIBroker"], FocusPolicy.HelloProcesses);
}
