using RoboB3.Application;
using RoboB3.Infrastructure;
using RoboB3.Workers.Jobs;

var builder = Host.CreateApplicationBuilder(args);

// Permite instalar como Serviço do Windows (sc.exe create ...); no console funciona normalmente.
builder.Services.AddWindowsService(options => options.ServiceName = "RoboB3 Workers");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHostedService<MarketDataWorker>();
builder.Services.AddHostedService<RiskMonitorWorker>();

await builder.Build().RunAsync();
