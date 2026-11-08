namespace BioTap.Core;

public enum FocusAction { None, Acquire, Release }

/// <summary>
/// Decides when the service holds sensor focus. Focus is released as soon as a Windows Hello prompt is
/// active and taken back only after no prompt has been seen for <c>reacquireDelay</c>.
/// Not thread-safe: call from a single loop.
/// </summary>
public sealed class FocusPolicy(TimeProvider time, TimeSpan reacquireDelay)
{
    /// <summary>Process names (no .exe) whose presence means Windows Hello may need the sensor (spec §2).</summary>
    public static readonly string[] HelloProcesses = ["LogonUI", "consent", "CredentialUIBroker", "UserOOBEBroker", "BioEnrollmentHost"];

    long? lastHelloSeen; // monotonic timestamp, immune to wall-clock jumps

    public bool HoldingFocus { get; private set; }

    public FocusAction Decide(bool helloActive)
    {
        if (helloActive)
        {
            lastHelloSeen = time.GetTimestamp();
            if (!HoldingFocus) return FocusAction.None;
            HoldingFocus = false;
            return FocusAction.Release;
        }
        if (HoldingFocus || (lastHelloSeen is { } seen && time.GetElapsedTime(seen) < reacquireDelay)) return FocusAction.None;
        HoldingFocus = true;
        return FocusAction.Acquire;
    }

    /// <summary>The caller could not perform <paramref name="action"/>; the next <see cref="Decide"/> asks again.</summary>
    public void Failed(FocusAction action)
    {
        if (action == FocusAction.Acquire) HoldingFocus = false;
        else if (action == FocusAction.Release) HoldingFocus = true;
    }
}
