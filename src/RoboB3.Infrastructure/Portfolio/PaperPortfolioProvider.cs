using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Paper;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Portfolio;

/// <summary>
/// Carteira do modo de execução vigente (simulado, MT5 demo ou real), marcada a mercado pelo último ciclo.
/// É ela que o monitor de risco vigia (perda do dia e drawdown).
/// </summary>
internal sealed class PaperPortfolioProvider(
    IPaperRepository paper,
    IExecutionSettingsRepository execution,
    IOptions<TradingCapitalOptions> options) : IPortfolioProvider
{
    public async Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var mode = (await execution.GetAsync(cancellationToken)).Mode;
        var account = await paper.GetAccountAsync(mode, cancellationToken);
        if (account is null)
        {
            var capital = options.Value.InitialCapital;
            return new PortfolioSnapshot(capital, capital, capital, []);
        }

        return account.Snapshot(await paper.ListPositionsAsync(mode, cancellationToken));
    }
}
