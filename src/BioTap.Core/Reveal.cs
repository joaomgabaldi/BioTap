namespace BioTap.Core;

/// <summary>
/// Progress of the panel/confirmation entrance and exit: 0 = hidden (7 px lower, transparent), 1 = shown.
/// Entering decelerates, leaving accelerates. A new Start mid-way continues from the current value and only
/// animates the remaining distance, so an interrupted transition never jumps. Pure, so it is testable.
/// </summary>
public sealed class Reveal
{
    double from, to;
    long startMs;
    double durationMs;

    public double Value { get; private set; }
    public bool Running { get; private set; }
    /// <summary>The state being shown or headed to.</summary>
    public bool Shown => to == 1;

    public void Start(bool show, long nowMs, int enterMs, int exitMs)
    {
        var target = show ? 1 : 0;
        if (Running) Value = At(nowMs);
        if (to == target && (Running || Value == target)) return;
        from = Value;
        to = target;
        startMs = nowMs;
        durationMs = (show ? enterMs : exitMs) * Math.Abs(target - from);
        Running = durationMs > 0;
        if (!Running) Value = target;
    }

    /// <summary>Show or hide without animation (system animations off).</summary>
    public void Jump(bool show)
    {
        to = Value = show ? 1 : 0;
        Running = false;
    }

    /// <summary>Value at <paramref name="nowMs"/>; Running turns false once the end is reached.</summary>
    public double Tick(long nowMs)
    {
        if (!Running) return Value;
        Value = At(nowMs);
        if (Value == to) Running = false;
        return Value;
    }

    double At(long nowMs)
    {
        var t = Math.Clamp((nowMs - startMs) / durationMs, 0, 1);
        if (t >= 1) return to;
        // Ease-out cubic to enter; ease-in quadratic to leave
        var eased = to == 1 ? 1 - Math.Pow(1 - t, 3) : t * t;
        return from + (to - from) * eased;
    }
}
