using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Portfolio;

/// <summary>
/// Carteira simulada da Fase 1: só o capital inicial, sem posições.
/// Será substituída pela carteira de paper trading (Fase 4) e pela do MT5 (Fase 5).
/// </summary>
internal sealed class SimulatedPortfolioProvider(IOptions<TradingOptions> options) : IPortfolioProvider
{
    public Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var capital = options.Value.InitialCapital;
        return Task.FromResult(new PortfolioSnapshot(capital, capital, capital, []));
    }
}
