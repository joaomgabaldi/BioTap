using System.Collections.Concurrent;
using BioTap.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BioTap.Service.Tests;

public class StopTests
{
    [Fact]
    public async Task Stop_WhenTheHostTimeoutAlreadyExpired_DoesNotThrow()
    {
        var sensor = new FakeSensor { IgnoreCancels = 1000 }; // Identify never unblocks
        var worker = new SensorWorker(sensor, () => false, new ConcurrentQueue<Message>().Enqueue, new ServiceConfig(800), new FakeTimeProvider(), NullLogger<SensorWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        using var expired = new CancellationTokenSource();
        expired.Cancel();
        await worker.StopAsync(expired.Token); // must not throw: an exception here crashes the service process on stop
        worker.Dispose();
    }
}
