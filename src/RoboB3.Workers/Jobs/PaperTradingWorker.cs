using Microsoft.Extensions.Options;
using RoboB3.Application.Paper;

namespace RoboB3.Workers.Jobs;

/// <summary>Roda o ciclo da operação simulada (preços reais do MT5, nenhuma ordem enviada à corretora).</summary>
public sealed class PaperTradingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<PaperTradingWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "paper-trading";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.PaperTradingIntervalSeconds);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken) =>
        services.GetRequiredService<PaperTradingService>().RunCycleAsync(stoppingToken);
}
