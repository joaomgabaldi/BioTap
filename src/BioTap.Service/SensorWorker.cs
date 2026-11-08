using BioTap.Core;

namespace BioTap.Service;

/// <summary>
/// Two loops: the sensor loop (blocking Identify → TapCounter, only while focus is held) and the focus loop
/// (every 250 ms: FocusPolicy decides whether to hold the sensor or leave it to Windows Hello).
/// Broadcasts taps and sensor status. Neither loop may die: Hello depends on the focus loop releasing the sensor.
/// </summary>
public sealed class SensorWorker : BackgroundService
{
    static readonly TimeSpan FocusTick = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan ReacquireDelay = TimeSpan.FromSeconds(1);
    static readonly TimeSpan SensorRetry = TimeSpan.FromSeconds(2);
    static readonly TimeSpan FocusRetry = TimeSpan.FromSeconds(5);
    static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(100);
    const int PresenceEveryTicks = 8; // 2 s

    readonly IBiometricSensor sensor;
    readonly Func<bool> helloActive;
    readonly Action<Message> broadcast;
    readonly TimeProvider time;
    readonly ILogger<SensorWorker> log;
    readonly TapCounter counter;
    readonly FocusPolicy policy;
    readonly Lock statusGate = new();
    SensorState? status;
    volatile bool sessionOpen;
    volatile bool focusHeld;
    bool present = true; // focus loop only
    Task? sensorLoop;

    public SensorWorker(IBiometricSensor sensor, Func<bool> helloActive, Action<Message> broadcast, ServiceConfig config, TimeProvider time, ILogger<SensorWorker> log)
    {
        this.sensor = sensor;
        this.helloActive = helloActive;
        this.broadcast = broadcast;
        this.time = time;
        this.log = log;
        counter = new TapCounter(config.TapTimeoutMs, time);
        counter.SequenceCompleted += n => Send(new TapsMessage(n));
        policy = new FocusPolicy(time, ReacquireDelay);
    }

    public void ApplyTimeout(SetTimeoutMessage m) => counter.TimeoutMs = m.Ms;

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        sensorLoop = Task.Run(() => SensorLoopAsync(ct), CancellationToken.None);
        return Task.WhenAll(sensorLoop, FocusLoopAsync(ct));
    }

    async Task SensorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!sessionOpen)
                {
                    if (sensor.Open()) sessionOpen = true;
                    else
                    {
                        SetStatus(SensorState.NoSensor);
                        await Task.Delay(SensorRetry, time, ct);
                    }
                    continue;
                }
                if (!focusHeld)
                {
                    await Task.Delay(IdleWait, time, ct); // without focus, touches belong to someone else
                    continue;
                }
                // Identify blocks: always run it on a pool thread, never inline on whatever thread completed the last timer
                switch (await Task.Run(sensor.Identify, CancellationToken.None))
                {
                    case IdentifyResult.Touch:
                        counter.Tap();
                        break;
                    case IdentifyResult.Canceled:
                        break; // focus released or stopping; the checks above decide what next
                    case IdentifyResult.DeviceError when !focusHeld:
                        break; // an Identify that raced the focus release fails; that is not a sensor fault
                    case IdentifyResult.DeviceError:
                        // Session is likely stale (sleep/resume, USB unplug): drop it and start over
                        sessionOpen = false;
                        sensor.Close();
                        SetStatus(SensorState.NoSensor);
                        await Task.Delay(SensorRetry, time, ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                log.LogError("Sensor loop error: {Error}", e.GetType().Name);
                try { await Task.Delay(SensorRetry, time, ct); } catch (OperationCanceledException) { }
            }
        }
    }

    async Task FocusLoopAsync(CancellationToken ct)
    {
        var retryAcquireAt = long.MinValue;
        var tick = 0;
        using var timer = new PeriodicTimer(FocusTick, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    if (tick++ % PresenceEveryTicks == 0) CheckPresence();
                    if (!sessionOpen)
                    {
                        // WinBioAcquireFocus is denied before the session is open. Focus is per process, not per
                        // session, so a lost session must release it explicitly or Hello stays without the sensor.
                        if (policy.HoldingFocus)
                        {
                            sensor.ReleaseFocus();
                            policy.Failed(FocusAction.Acquire);
                        }
                        focusHeld = false;
                        continue;
                    }
                    var hello = IsHelloActive();
                    if (!hello && !policy.HoldingFocus && time.GetTimestamp() < retryAcquireAt) continue;
                    switch (policy.Decide(hello))
                    {
                        case FocusAction.Acquire:
                            var hr = sensor.AcquireFocus();
                            if (hr == 0)
                            {
                                focusHeld = true;
                                SetStatus(present ? SensorState.Ok : SensorState.NoSensor);
                            }
                            else
                            {
                                policy.Failed(FocusAction.Acquire);
                                retryAcquireAt = time.GetTimestamp() + (long)(FocusRetry.TotalSeconds * time.TimestampFrequency);
                                log.LogWarning("WinBioAcquireFocus hr=0x{Hr:X8}", hr);
                                SetStatus(SensorState.NoFocus);
                            }
                            break;
                        case FocusAction.Release:
                            focusHeld = false; // stop the sensor loop from re-entering Identify
                            sensor.Cancel();
                            if (sensor.ReleaseFocus() == 0) SetStatus(SensorState.ReleasedForHello);
                            else policy.Failed(FocusAction.Release);
                            sensor.Cancel(); // in case an Identify slipped in between
                            break;
                    }
                }
                catch (Exception e)
                {
                    log.LogError("Focus loop error: {Error}", e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>An unplugged sensor leaves Identify blocked without an error, so presence is polled.</summary>
    void CheckPresence()
    {
        bool now;
        try { now = sensor.IsPresent(); }
        catch (Exception e)
        {
            log.LogWarning("Sensor presence check failed ({Error})", e.GetType().Name);
            return;
        }
        if (now == present) return;
        present = now;
        if (!now) SetStatus(SensorState.NoSensor);
        else if (focusHeld) SetStatus(SensorState.Ok);
        else if (sessionOpen && !policy.HoldingFocus) SetStatus(SensorState.ReleasedForHello);
    }

    bool IsHelloActive()
    {
        try
        {
            return helloActive();
        }
        catch (Exception e)
        {
            log.LogWarning("Hello detection failed ({Error}); releasing the sensor to be safe", e.GetType().Name);
            return true;
        }
    }

    void SetStatus(SensorState s)
    {
        lock (statusGate)
        {
            if (status == s) return;
            status = s;
        }
        Send(new StatusMessage(s));
    }

    void Send(Message m)
    {
        try
        {
            broadcast(m);
        }
        catch (Exception e)
        {
            log.LogError("Broadcast failed: {Error}", e.GetType().Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopping = base.StopAsync(cancellationToken);
        // A cancel sent before Identify starts blocking is lost, so keep cancelling until the sensor loop exits.
        while (sensorLoop is { IsCompleted: false } && !cancellationToken.IsCancellationRequested)
        {
            sensor.Cancel();
            await Task.WhenAny(sensorLoop, Task.Delay(100, CancellationToken.None));
        }
        await stopping;
        if (focusHeld) sensor.ReleaseFocus();
    }

    public override void Dispose()
    {
        counter.Dispose();
        sensor.Close(); // the DI container owns and disposes the sensor
        base.Dispose();
    }
}
