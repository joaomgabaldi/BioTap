using BioTap.Core;
using BioTap.Service;

var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BioTap", "config.json");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = PipeClient.ServiceName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBiometricSensor, WinBioSensor>();
builder.Services.AddSingleton(sp => new PipeServer(PipeServer.DefaultName, sp.GetRequiredService<ILogger<PipeServer>>()));
builder.Services.AddSingleton(sp => ServiceConfig.Load(configPath, AclGuard.IsWritableOnlyByAdmins, sp.GetRequiredService<ILogger<ServiceConfig>>()));
builder.Services.AddSingleton(sp =>
{
    var pipe = sp.GetRequiredService<PipeServer>();
    var worker = new SensorWorker(sp.GetRequiredService<IBiometricSensor>(), HelloWatcher.IsHelloActive, pipe.Broadcast,
        sp.GetRequiredService<ServiceConfig>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<SensorWorker>>());
    pipe.TimeoutRequested += worker.ApplyTimeout;
    return worker;
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorWorker>());
builder.Services.AddHostedService<PipeHost>();
builder.Build().Run();

/// <summary>Runs the pipe server; if it cannot own the pipe name (squatting), the service stops (fail closed).</summary>
sealed class PipeHost(PipeServer server) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => server.RunAsync(ct);
}
