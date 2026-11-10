using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Tests;

public sealed class WindowPresetTests
{
    readonly FakeWindows w = new();

    [Fact]
    public void Topmost_NoForeground_IsNotReady() => Assert.Equal("Nenhuma janela em foco", new TopmostPreset(w).CheckReady([]));

    [Fact]
    public async Task Topmost_TogglesTheForegroundWindow()
    {
        w.ForegroundWindow = 42;
        Assert.Null(new TopmostPreset(w).CheckReady([]));
        await new TopmostPreset(w).RunAsync([], default);
        Assert.Equal(["Topmost(42)"], w.Calls);
    }

    [Fact]
    public void MinimizeOthers_NoForeground_IsNotReady() => Assert.Equal("Nenhuma janela em foco", new MinimizeOthersPreset(w).CheckReady([]));

    [Fact]
    public async Task MinimizeOthers_KeepsTheForegroundWindow()
    {
        w.ForegroundWindow = 42;
        await new MinimizeOthersPreset(w).RunAsync([], default);
        Assert.Equal(["MinimizeExcept(42)"], w.Calls);
    }

    [Fact]
    public async Task Lock_CallsLock()
    {
        await new LockPreset(w).RunAsync([], default);
        Assert.Equal(["Lock"], w.Calls);
    }

    [Fact]
    public void AllThree_ValidateEmptyParams()
    {
        Assert.Null(new TopmostPreset(w).ValidateParams([]));
        Assert.Null(new MinimizeOthersPreset(w).ValidateParams([]));
        Assert.Null(new LockPreset(w).ValidateParams([]));
    }

    [Fact]
    public void Lock_IsReady_WithoutForegroundWindow() => Assert.Null(new LockPreset(w).CheckReady([]));

    [Fact]
    public async Task ForegroundLostBetweenCheckAndRun_Throws_AndNeverMinimizesEverything()
    {
        w.Sequence = new Queue<nint>([42, 0]);
        Assert.Null(new MinimizeOthersPreset(w).CheckReady([]));
        await Assert.ThrowsAsync<PresetException>(() => new MinimizeOthersPreset(w).RunAsync([], default));
        w.Sequence = new Queue<nint>([0]);
        await Assert.ThrowsAsync<PresetException>(() => new TopmostPreset(w).RunAsync([], default));
        Assert.Empty(w.Calls);
    }
}
