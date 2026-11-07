using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Risk;

public sealed class RiskSettingsService(
    IRiskSettingsRepository settings,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<RiskLimits> GetLimitsAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync(cancellationToken)).Limits;

    public async Task<RiskLimits> UpdateLimitsAsync(RiskLimits limits, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var current = await settings.GetAsync(cancellationToken);
        var previous = current.Limits;
        var now = clock.GetUtcNow();
        current.Update(limits, now);
        audit.Record(new AuditEntry("risk-limits.update", actor, $"{Describe(previous)} -> {Describe(limits)}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return current.Limits;
    }

    private static string Describe(RiskLimits l) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"perda/dia {l.MaxDailyLossPercent}%, drawdown {l.MaxDrawdownPercent}%, por ativo {l.MaxPositionPercentPerAsset}%, posições {l.MaxOpenPositions}, futuros {l.MaxFuturesContracts}");
}
