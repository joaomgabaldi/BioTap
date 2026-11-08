using BioTap.Core;

namespace BioTap.Service;

/// <summary>
/// Two loops: the sensor loop (blocking Identify → TapCounter) and the focus loop (every 250 ms: FocusPolicy decides
/// whether to hold the sensor or leave it to Windows Hello). Broadcasts taps and sensor status.
/// </summary>
public sealed class SensorWorker : BackgroundService
{
    static readonly TimeSpan FocusTick = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan ReacquireDelay = TimeSpan.FromSeconds(1);
    static readonly TimeSpan SensorRetry = TimeSpan.FromSeconds(2);
    static readonly TimeSpan FocusRetry = TimeSpan.FromSeconds(5);

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
    Task? sensorLoop;

    public SensorWorker(IBiometricSensor sensor, Func<bool> helloActive, Action<Message> broadcast, ServiceConfig config, TimeProvider time, ILogger<SensorWorker> log)
    {
        this.sensor = sensor;
        this.helloActive = helloActive;
        this.broadcast = broadcast;
        this.time = time;
        this.log = log;
        counter = new TapCounter(config.TapTimeoutMs, time);
        counter.SequenceCompleted += n => broadcast(new TapsMessage(n));
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
        try
        {
            while (!ct.IsCancellationRequested && !sensor.Open())
            {
                SetStatus(SensorState.NoSensor);
                await Task.Delay(SensorRetry, time, ct);
            }
            sessionOpen = true;
            while (!ct.IsCancellationRequested)
            {
                switch (sensor.Identify())
                {
                    case IdentifyResult.Touch:
                        counter.Tap();
                        break;
                    case IdentifyResult.DeviceError:
                        SetStatus(SensorState.NoSensor);
                        await Task.Delay(SensorRetry, time, ct);
                        if (policy.HoldingFocus) SetStatus(SensorState.Ok);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    async Task FocusLoopAsync(CancellationToken ct)
    {
        var retryAcquireAt = long.MinValue;
        using var timer = new PeriodicTimer(FocusTick, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!sessionOpen) continue; // WinBioAcquireFocus is denied before the session is open
                var hello = helloActive();
                if (!hello && !policy.HoldingFocus && time.GetTimestamp() < retryAcquireAt) continue;
                switch (policy.Decide(hello))
                {
                    case FocusAction.Acquire:
                        var hr = sensor.AcquireFocus();
                        if (hr == 0)
                        {
                            SetStatus(SensorState.Ok);
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
                        sensor.Cancel();
                        if (sensor.ReleaseFocus() == 0) SetStatus(SensorState.ReleasedForHello);
                        else policy.Failed(FocusAction.Release);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    void SetStatus(SensorState s)
    {
        lock (statusGate)
        {
            if (status == s) return;
            status = s;
        }
        broadcast(new StatusMessage(s));
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
        if (policy.HoldingFocus) sensor.ReleaseFocus();
    }

    public override void Dispose()
    {
        counter.Dispose();
        sensor.Dispose();
        base.Dispose();
    }
}
