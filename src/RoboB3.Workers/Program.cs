using System.Globalization;
using RoboB3.Application;
using RoboB3.Infrastructure;
using RoboB3.Workers.Jobs;

// Mensagens de negócio (motivos, eventos, decisões da IA) no formato brasileiro: 1,27 e não 1.27.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

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
builder.Services.AddHostedService<LearningWorker>();
builder.Services.AddHostedService<PaperTradingWorker>();
builder.Services.AddHostedService<IntradayDataWorker>();
builder.Services.AddHostedService<TelegramNotificationWorker>();
builder.Services.AddHostedService<TelegramCommandWorker>();

await builder.Build().RunAsync();
