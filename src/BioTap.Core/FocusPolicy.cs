namespace BioTap.Core;

public enum FocusAction { None, Acquire, Release }

/// <summary>
/// Decides when the service holds sensor focus. Focus is released as soon as a Windows Hello prompt is
/// active and taken back only after no prompt has been seen for <c>reacquireDelay</c>.
/// </summary>
public sealed class FocusPolicy(TimeProvider time, TimeSpan reacquireDelay)
{
    /// <summary>Process names (no .exe) whose presence means Windows Hello may need the sensor (spec §2).</summary>
    public static readonly string[] HelloProcesses = ["LogonUI", "consent", "CredentialUIBroker"];

    DateTimeOffset? lastHelloSeen;

    public bool HoldingFocus { get; private set; }

    public FocusAction Decide(bool helloActive)
    {
        var now = time.GetUtcNow();
        if (helloActive)
        {
            lastHelloSeen = now;
            if (!HoldingFocus) return FocusAction.None;
            HoldingFocus = false;
            return FocusAction.Release;
        }
        if (HoldingFocus || (lastHelloSeen is { } seen && now - seen < reacquireDelay)) return FocusAction.None;
        HoldingFocus = true;
        return FocusAction.Acquire;
    }
}
