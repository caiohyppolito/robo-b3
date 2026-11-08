using Microsoft.Extensions.Options;
using RoboB3.Application.MarketData;

namespace RoboB3.Workers.Jobs;

/// <summary>Coleta candles diários de ações, ETFs e índices.</summary>
public sealed partial class MarketDataWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<MarketDataWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "market-data";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(options.Value.MarketDataIntervalMinutes);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        var result = await services.GetRequiredService<MarketDataIngestionService>().IngestDailyAsync(stoppingToken);
        LogResult(result.AssetsProcessed, result.CandlesSaved, result.Failures.Count);

        // CDI do Banco Central: falha aqui não atrapalha a coleta de preços.
        try
        {
            await services.GetRequiredService<RiskFreeRateService>().SyncAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException)
        {
            LogCdiFailed(ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "CDI do Banco Central indisponível: {Message}. O backtest usa a taxa fixa configurada.")]
    private partial void LogCdiFailed(string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Coleta concluída: {Assets} ativos, {Candles} candles gravados, {Failures} falhas")]
    private partial void LogResult(int assets, int candles, int failures);
}
