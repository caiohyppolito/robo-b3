using Microsoft.Extensions.Options;
using RoboB3.Application.Paper;

namespace RoboB3.Workers.Jobs;

/// <summary>Grava barras intradiárias do MT5 (WIN/WDO e tempos gráficos das alocações) para backtest.</summary>
public sealed partial class IntradayDataWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<IntradayDataWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "intraday-data";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(options.Value.IntradayDataIntervalMinutes);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        var saved = await services.GetRequiredService<IntradayIngestionService>().IngestAsync(stoppingToken);
        if (saved > 0)
        {
            LogSaved(saved);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Barras intradiárias gravadas: {Count}")]
    private partial void LogSaved(int count);
}
