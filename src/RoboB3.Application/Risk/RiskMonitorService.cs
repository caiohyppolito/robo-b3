using Microsoft.Extensions.Logging;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Risk;

/// <summary>Verifica os limites globais e aciona o kill switch automaticamente quando estourados.</summary>
public sealed partial class RiskMonitorService(
    IPortfolioProvider portfolio,
    IRiskSettingsRepository settings,
    KillSwitchService killSwitch,
    ILogger<RiskMonitorService> logger)
{
    public const string Actor = "risk-monitor";

    /// <returns>Verdadeiro se o robô foi travado nesta verificação.</returns>
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        var state = await killSwitch.GetStateAsync(cancellationToken);
        if (state.IsHalted)
        {
            return false;
        }

        var snapshot = await portfolio.GetSnapshotAsync(cancellationToken);
        var limits = (await settings.GetAsync(cancellationToken)).Limits;
        var breach = RiskManager.FindGlobalBreach(snapshot, limits);
        if (breach is null)
        {
            return false;
        }

        LogBreach(breach);
        await killSwitch.HaltAsync(breach, Actor, cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Limite de risco estourado, travando o robô: {Breach}")]
    private partial void LogBreach(string breach);
}
