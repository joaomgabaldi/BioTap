using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>Window operations (Win32 in the App; a fake in tests).</summary>
public interface IWindows
{
    /// <summary>The foreground window, or 0 when it is the desktop, the taskbar or there is none.</summary>
    nint Foreground();
    void ToggleTopmost(nint window);
    /// <summary>Minimizes every ordinary window except <paramref name="window"/> and BioTap's own.</summary>
    void MinimizeAllExcept(nint window);
    void Lock();
}

/// <summary>Base for parameterless presets that act on the foreground window.</summary>
public abstract class WindowPreset(string id, string name, string glyph, IWindows windows) : IPreset
{
    protected IWindows Windows => windows;
    public string Id => id;
    public string DisplayName => name;
    public IconRef DefaultIcon => new("glyph", glyph);
    public string? ValidateParams(JsonObject p) => null;
    public virtual string? CheckReady(JsonObject p) => windows.Foreground() == 0 ? "Nenhuma janela em foco" : null;
    public abstract Task RunAsync(JsonObject p, CancellationToken ct);
}

public sealed class TopmostPreset(IWindows w) : WindowPreset("topmost", "Sempre no topo", "E718", w)
{
    public override Task RunAsync(JsonObject p, CancellationToken ct) { Windows.ToggleTopmost(Windows.Foreground()); return Task.CompletedTask; }
}

public sealed class MinimizeOthersPreset(IWindows w) : WindowPreset("minimizeOthers", "Minimizar as outras janelas", "E73F", w)
{
    public override Task RunAsync(JsonObject p, CancellationToken ct) { Windows.MinimizeAllExcept(Windows.Foreground()); return Task.CompletedTask; }
}

public sealed class LockPreset(IWindows w) : WindowPreset("lock", "Bloquear o Windows", "E72E", w)
{
    public override string? CheckReady(JsonObject p) => null;
    public override Task RunAsync(JsonObject p, CancellationToken ct) { Windows.Lock(); return Task.CompletedTask; }
}
