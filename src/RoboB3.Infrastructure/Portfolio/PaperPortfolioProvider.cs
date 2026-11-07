using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Paper;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Portfolio;

/// <summary>
/// Carteira da conta simulada, marcada a mercado pelo último ciclo de operação.
/// É ela que o monitor de risco vigia (perda do dia e drawdown).
/// </summary>
internal sealed class PaperPortfolioProvider(IPaperRepository paper, IOptions<TradingCapitalOptions> options) : IPortfolioProvider
{
    public async Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var account = await paper.GetAccountAsync(cancellationToken);
        if (account is null)
        {
            var capital = options.Value.InitialCapital;
            return new PortfolioSnapshot(capital, capital, capital, []);
        }

        return account.Snapshot(await paper.ListPositionsAsync(cancellationToken));
    }
}
