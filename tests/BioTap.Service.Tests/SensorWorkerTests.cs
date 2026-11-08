using System.Collections.Concurrent;
using BioTap.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BioTap.Service.Tests;

public sealed class SensorWorkerTests : IAsyncDisposable
{
    readonly FakeTimeProvider time = new();
    readonly FakeSensor sensor = new();
    readonly ConcurrentQueue<Message> sent = new();
    volatile bool hello;
    SensorWorker? worker;

    SensorWorker Start(int timeoutMs = 800)
    {
        worker = new SensorWorker(sensor, () => hello, sent.Enqueue, new ServiceConfig(timeoutMs), time, NullLogger<SensorWorker>.Instance);
        worker.StartAsync(CancellationToken.None).Wait();
        return worker;
    }

    static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    /// <summary>Advances fake time in focus-loop ticks, letting async continuations run between ticks.</summary>
    async Task Tick(int ms)
    {
        for (var t = 0; t < ms; t += 250)
        {
            time.Advance(TimeSpan.FromMilliseconds(Math.Min(250, ms - t)));
            await Task.Delay(15);
        }
    }

    /// <summary>Ticks until the condition holds (the sensor loop opens the session asynchronously).</summary>
    async Task TickUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Tick(250);
        Assert.True(condition());
    }

    bool Got(Message m) => sent.Contains(m);

    public async ValueTask DisposeAsync()
    {
        if (worker is not null)
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Startup_OpensSessionBeforeAcquiringFocus()
    {
        Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => sensor.Count("AcquireFocus") == 1);
        var calls = sensor.Calls.ToList();
        Assert.True(calls.IndexOf("Open") < calls.IndexOf("AcquireFocus"));
        await WaitUntil(() => Got(new StatusMessage(SensorState.Ok)));
    }

    [Fact]
    public async Task Touches_AreCountedAndBroadcastAsTaps()
    {
        Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => sensor.BlockedInIdentify);
        sensor.Push(IdentifyResult.Touch);
        sensor.Push(IdentifyResult.Touch);
        await WaitUntil(() => sensor.Count("Identify") >= 3);
        await Tick(800);
        await WaitUntil(() => Got(new TapsMessage(2)));
    }

    [Fact]
    public async Task HelloActive_WhileIdentifyBlocked_CancelsThenReleases()
    {
        Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => sensor.BlockedInIdentify);
        hello = true;
        await Tick(250);
        await WaitUntil(() => sensor.Count("ReleaseFocus") == 1);
        var calls = sensor.Calls.ToList();
        Assert.True(calls.LastIndexOf("Cancel") < calls.IndexOf("ReleaseFocus"));
        await WaitUntil(() => Got(new StatusMessage(SensorState.ReleasedForHello)));
    }

    [Fact]
    public async Task HelloGone_ReacquiresAndReportsOk()
    {
        Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        hello = true;
        await Tick(250);
        await WaitUntil(() => sensor.Count("ReleaseFocus") == 1);
        while (sent.TryDequeue(out _)) { }
        hello = false;
        await Tick(1250);
        await WaitUntil(() => sensor.Count("AcquireFocus") == 2);
        await WaitUntil(() => Got(new StatusMessage(SensorState.Ok)));
    }

    [Fact]
    public async Task SensorLoop_DeviceError_BacksOffAndReportsNoSensor()
    {
        sensor.AlwaysReturn = IdentifyResult.DeviceError;
        Start();
        await Tick(10_000);
        Assert.InRange(sensor.Count("Identify"), 1, 6);
        Assert.True(Got(new StatusMessage(SensorState.NoSensor)));
    }

    [Fact]
    public async Task OpenFails_ReportsNoSensorAndRetries()
    {
        sensor.OpenSucceeds = false;
        Start();
        await Tick(4000);
        Assert.InRange(sensor.Count("Open"), 2, 3);
        Assert.Equal(0, sensor.Count("AcquireFocus"));
        Assert.True(Got(new StatusMessage(SensorState.NoSensor)));
    }

    [Fact]
    public async Task AcquireFocusDenied_ReportsNoFocusAndRetriesEvery5s()
    {
        sensor.AcquireHr = unchecked((int)0x80070005);
        Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => Got(new StatusMessage(SensorState.NoFocus)));
        await Tick(4500);
        Assert.Equal(1, sensor.Count("AcquireFocus"));
        await Tick(750);
        await WaitUntil(() => sensor.Count("AcquireFocus") == 2);
    }

    [Fact]
    public async Task SetTimeout_UpdatesCounter()
    {
        var w = Start();
        w.ApplyTimeout(new SetTimeoutMessage(1500));
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => sensor.BlockedInIdentify);
        sensor.Push(IdentifyResult.Touch);
        await WaitUntil(() => sensor.Count("Identify") >= 2);
        await Tick(1000);
        Assert.DoesNotContain(sent, m => m is TapsMessage);
        await Tick(750);
        await WaitUntil(() => Got(new TapsMessage(1)));
    }

    [Fact]
    public async Task Stop_CancelRace_ReissuesCancelUntilLoopExits()
    {
        var w = Start();
        await TickUntil(() => sensor.Count("AcquireFocus") >= 1);
        await WaitUntil(() => sensor.BlockedInIdentify);
        sensor.IgnoreCancels = 2; // the first cancels arrive "too early" and are lost
        var stop = w.StopAsync(CancellationToken.None);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(sensor.Count("Cancel") >= 3);
        worker = null;
        w.Dispose();
    }
}
