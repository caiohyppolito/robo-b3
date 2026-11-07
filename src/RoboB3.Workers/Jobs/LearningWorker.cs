using Microsoft.Extensions.Options;
using RoboB3.Application.Learning;
using RoboB3.Domain.Common;
using RoboB3.Domain.Strategies;

namespace RoboB3.Workers.Jobs;

/// <summary>Retreina periodicamente o filtro de IA de cada estratégia e promove só o que melhorar.</summary>
public sealed partial class LearningWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<LearningWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "learning";

    protected override TimeSpan Interval => TimeSpan.FromHours(options.Value.LearningIntervalHours);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        var learning = services.GetRequiredService<LearningService>();
        foreach (var strategy in StrategyCatalog.All.Where(s => s.Key != StrategyKeys.BuyAndHold))
        {
            try
            {
                var model = await learning.TrainAsync(strategy.Key, stoppingToken);
                LogTrained(strategy.Key, model.Status.ToString(), model.Decision);
            }
            catch (DomainException ex)
            {
                LogSkipped(strategy.Key, ex.Message);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IA {Strategy}: {Status}. {Decision}")]
    private partial void LogTrained(string strategy, string status, string decision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IA {Strategy} não treinada: {Reason}")]
    private partial void LogSkipped(string strategy, string reason);
}
