using BioTap.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "BioTap.Service");
builder.Services.AddHostedService<SensorProbeWorker>();
builder.Build().Run();
