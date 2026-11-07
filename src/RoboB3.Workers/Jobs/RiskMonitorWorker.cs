using Microsoft.Extensions.Options;
using RoboB3.Application.Risk;

namespace RoboB3.Workers.Jobs;

/// <summary>Vigia perda diária e drawdown; trava o robô automaticamente quando estourados.</summary>
public sealed class RiskMonitorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<RiskMonitorWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "risk-monitor";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.RiskMonitorIntervalSeconds);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken) =>
        services.GetRequiredService<RiskMonitorService>().CheckAsync(stoppingToken);
}
