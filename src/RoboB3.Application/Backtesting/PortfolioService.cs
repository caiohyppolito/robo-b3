using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.Backtesting;

public sealed record PortfolioRequest(
    IReadOnlyList<string> Symbols,
    DateOnly? From,
    DateOnly? To,
    PortfolioWeighting Weighting = PortfolioWeighting.InverseVolatility,
    decimal MaxWeightPercent = 20m,
    bool TrendFilter = false,
    decimal InitialCapital = 100_000m,
    bool Fractional = true);

public sealed record PortfolioResponse(
    DateOnly From,
    DateOnly To,
    PortfolioResult Result,
    IReadOnlyList<string> Skipped);

/// <summary>Monta as séries (com histórico de aquecimento antes do início) e roda o backtest de carteira com os custos e o CDI do sistema.</summary>
public sealed class PortfolioService(BacktestService backtests)
{
    public async Task<PortfolioResponse> RunAsync(PortfolioRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Symbols is not { Count: > 0 })
        {
            throw new DomainException("Escolha pelo menos um ativo.");
        }

        if (request.Symbols.Count > 60)
        {
            throw new DomainException("No máximo 60 ativos por carteira.");
        }

        var (from, to) = backtests.ResolvePeriod(request.From, request.To);
        var assets = new List<PortfolioAsset>();
        var skipped = new List<string>();
        foreach (var symbol in request.Symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var (asset, bars) = await backtests.LoadBarsAsync(symbol, from, to, cancellationToken);
                if (asset.IsFuture)
                {
                    skipped.Add($"{asset.Symbol}: futuros não entram na carteira.");
                    continue;
                }

                assets.Add(new PortfolioAsset(InstrumentSpec.For(asset), bars));
            }
            catch (DomainException ex)
            {
                skipped.Add($"{symbol}: {ex.Message}");
            }
        }

        if (assets.Count == 0)
        {
            throw new DomainException("Nenhum ativo com histórico no período. " + string.Join(" ", skipped));
        }

        var settings = await backtests.BuildSettingsAsync(new SimulationSettings(), cancellationToken, period: (from, to));
        var portfolio = new PortfolioSettings(request.InitialCapital, request.Weighting, request.MaxWeightPercent, request.TrendFilter,
            settings.Costs, settings.RiskFreeRatePercent, settings.RiskFreeCurve, Fractional: request.Fractional);
        return new PortfolioResponse(from, to, PortfolioBacktest.Run(assets, portfolio, from), skipped);
    }
}
