using RoboB3.Domain.Analysis;
using RoboB3.Domain.Common;

namespace RoboB3.Domain.Backtesting;

public enum PortfolioWeighting
{
    /// <summary>Mesmo peso para cada ativo elegível.</summary>
    Equal = 1,

    /// <summary>Peso proporcional ao inverso da volatilidade de 60 dias (paridade de risco simples).</summary>
    InverseVolatility = 2,
}

public sealed record PortfolioSettings(
    decimal InitialCapital,
    PortfolioWeighting Weighting,
    decimal MaxWeightPercent,
    bool TrendFilter,
    CostModel Costs,
    decimal RiskFreeRatePercent,
    RiskFreeCurve? RiskFreeCurve = null,
    int VolatilityLookback = 60,
    int TrendPeriod = 200,
    bool Fractional = true);

public sealed record PortfolioAsset(InstrumentSpec Instrument, IReadOnlyList<Bar> Bars);

/// <param name="NotBought">Ativos com peso-alvo que não couberam num lote (com o fracionário desligado ou capital pequeno).</param>
public sealed record PortfolioRebalance(DateOnly Date, IReadOnlyDictionary<string, decimal> TargetWeights, decimal CashWeight, decimal Turnover, decimal Costs, IReadOnlyList<string> NotBought);

public sealed record PortfolioResult(
    IReadOnlyList<EquityPoint> Equity,
    PerformanceMetrics Metrics,
    IReadOnlyList<PortfolioRebalance> Rebalances,
    IReadOnlyDictionary<string, decimal> FinalWeights,
    decimal AverageInvestedPercent);

/// <summary>
/// Carteira de vários ativos (ações e ETFs), rebalanceada no primeiro pregão de cada mês.
/// Os pesos usam só dados até o fechamento ANTERIOR e as ordens executam na abertura do dia, com slippage,
/// corretagem, taxas e lotes. Teto por ativo: o que passa do teto (ou o que o filtro de tendência tira) fica
/// no caixa, rendendo CDI. Sem IR (ver o relatório de IR para a apuração das operações do robô).
/// Comparada com a mesma cesta comprada e segurada com pesos iguais e com o CDI.
/// </summary>
public static class PortfolioBacktest
{
    public static PortfolioResult Run(IReadOnlyList<PortfolioAsset> assets, PortfolioSettings settings, DateOnly from)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(settings);
        if (assets.Count == 0)
        {
            throw new DomainException("Escolha pelo menos um ativo.");
        }

        if (assets.Any(a => a.Instrument.IsFuture))
        {
            throw new DomainException("Carteira é só de ações e ETFs; futuros ficam nas alocações do robô.");
        }

        Guard.Positive(settings.InitialCapital, nameof(settings.InitialCapital));
        var cap = Guard.Percentage(settings.MaxWeightPercent, nameof(settings.MaxWeightPercent)) / 100m;

        var dates = assets.SelectMany(a => a.Bars.Select(b => b.Date)).Where(d => d >= from).Distinct().Order().ToList();
        if (dates.Count < 2)
        {
            throw new DomainException("Histórico insuficiente no período.");
        }

        // Índice de cada data em cada ativo, para olhar só o passado.
        var lookup = assets.ToDictionary(a => a.Instrument.Symbol, a => a.Bars.Select((b, i) => (b.Date, i)).ToDictionary(x => x.Date, x => x.i));
        var holdings = assets.ToDictionary(a => a.Instrument.Symbol, _ => 0);
        var lastClose = new Dictionary<string, decimal>();
        var cash = settings.InitialCapital;
        decimal costsPaid = 0, interest = 0, investedSum = 0;
        var constantDaily = RiskFreeCurve.Constant(settings.RiskFreeRatePercent);
        var riskFreeIndex = 1m;
        var equity = new List<EquityPoint>();
        var rebalances = new List<PortfolioRebalance>();

        // Comparação: mesma cesta, pesos iguais, comprada no primeiro dia e nunca mexida.
        var holdShares = new Dictionary<string, decimal>();
        var holdCash = settings.InitialCapital;

        for (var d = 0; d < dates.Count; d++)
        {
            var date = dates[d];
            if (d > 0)
            {
                var rate = settings.RiskFreeCurve?.DailyRate(dates[d - 1]) ?? constantDaily;
                riskFreeIndex *= 1m + rate;
                if (cash > 0)
                {
                    var earned = cash * rate;
                    cash += earned;
                    interest += earned;
                }

                if (holdCash > 0)
                {
                    holdCash += holdCash * rate;
                }
            }

            var isRebalanceDay = d == 0 || date.Month != dates[d - 1].Month;
            if (isRebalanceDay)
            {
                var targets = TargetWeights(assets, lookup, date, settings, cap);
                var equityAtOpen = cash + assets.Sum(a => holdings[a.Instrument.Symbol] * OpenOrLast(a, lookup, date, lastClose) * a.Instrument.Multiplier);
                decimal traded = 0, cost = 0;
                var notBought = new List<string>();

                // Vende primeiro (libera caixa), depois compra.
                foreach (var sellFirst in new[] { true, false })
                {
                    foreach (var asset in assets)
                    {
                        var symbol = asset.Instrument.Symbol;
                        if (!lookup[symbol].TryGetValue(date, out var index))
                        {
                            continue;
                        }

                        var open = asset.Bars[index].Open;
                        var weight = targets.GetValueOrDefault(symbol);
                        // No fracionário (código com F) ações andam de 1 em 1; no lote padrão, de 100 em 100.
                        var lot = settings.Fractional ? 1 : asset.Instrument.LotSize;
                        var desired = (int)Math.Floor(equityAtOpen * weight / (open * asset.Instrument.Multiplier) / lot) * lot;
                        if (sellFirst && weight > 0 && desired == 0)
                        {
                            notBought.Add(symbol);
                        }

                        var delta = desired - holdings[symbol];
                        if (delta == 0 || (delta < 0) != sellFirst)
                        {
                            continue;
                        }

                        var isBuy = delta > 0;
                        var price = settings.Costs.Fill(open, isBuy);
                        var notional = Math.Abs(delta) * price * asset.Instrument.Multiplier;
                        var orderCost = settings.Costs.OrderCost(notional, Math.Abs(delta), false);
                        if (isBuy && notional + orderCost > cash)
                        {
                            var affordable = (int)Math.Floor((cash - orderCost) / (price * asset.Instrument.Multiplier) / lot) * lot;
                            if (affordable <= 0)
                            {
                                continue;
                            }

                            delta = affordable;
                            notional = delta * price * asset.Instrument.Multiplier;
                            orderCost = settings.Costs.OrderCost(notional, delta, false);
                        }

                        cash += isBuy ? -(notional + orderCost) : notional - orderCost;
                        holdings[symbol] += delta;
                        traded += notional;
                        cost += orderCost;
                    }
                }

                costsPaid += cost;
                rebalances.Add(new PortfolioRebalance(date, targets, Math.Round(1m - targets.Values.Sum(), 4),
                    equityAtOpen > 0 ? Math.Round(traded / equityAtOpen, 4) : 0, Math.Round(cost, 2), notBought));
            }

            foreach (var asset in assets)
            {
                if (lookup[asset.Instrument.Symbol].TryGetValue(date, out var index))
                {
                    lastClose[asset.Instrument.Symbol] = asset.Bars[index].Close;
                }
            }

            if (d == 0 || holdShares.Count < assets.Count)
            {
                // A cesta de comparação compra cada ativo no primeiro dia em que ele tem preço.
                foreach (var asset in assets.Where(a => !holdShares.ContainsKey(a.Instrument.Symbol) && lastClose.ContainsKey(a.Instrument.Symbol)))
                {
                    var slice = settings.InitialCapital / assets.Count;
                    holdShares[asset.Instrument.Symbol] = slice / lastClose[asset.Instrument.Symbol];
                    holdCash -= slice;
                }
            }

            var invested = assets.Sum(a => holdings[a.Instrument.Symbol] * lastClose.GetValueOrDefault(a.Instrument.Symbol) * a.Instrument.Multiplier);
            var total = cash + invested;
            investedSum += total > 0 ? invested / total : 0;
            var benchmark = holdCash + holdShares.Sum(h => h.Value * lastClose.GetValueOrDefault(h.Key));
            equity.Add(new EquityPoint(date, Math.Round(total, 2), Math.Round(benchmark, 2), Math.Round(settings.InitialCapital * riskFreeIndex, 2)));
        }

        var finalTotal = equity[^1].Equity;
        var finalWeights = assets.ToDictionary(
            a => a.Instrument.Symbol,
            a => finalTotal > 0 ? Math.Round(holdings[a.Instrument.Symbol] * lastClose.GetValueOrDefault(a.Instrument.Symbol) * a.Instrument.Multiplier / finalTotal, 4) : 0m);
        var riskFreePercent = dates.Count > 1
            ? Math.Round(((decimal)Math.Pow((double)riskFreeIndex, (double)PerformanceMetrics.TradingDaysPerYear / (dates.Count - 1)) - 1m) * 100m, 4)
            : settings.RiskFreeRatePercent;
        var metrics = PerformanceMetrics.Calculate(equity, [], dates.Count, dates.Count, Math.Round(costsPaid, 2), 0, Math.Round(interest, 2), riskFreePercent);
        return new PortfolioResult(equity, metrics, rebalances, finalWeights, Math.Round(100m * investedSum / dates.Count, 1));
    }

    /// <summary>Pesos-alvo com dados até o pregão anterior a <paramref name="date"/>.</summary>
    internal static Dictionary<string, decimal> TargetWeights(
        IReadOnlyList<PortfolioAsset> assets,
        IReadOnlyDictionary<string, Dictionary<DateOnly, int>> lookup,
        DateOnly date,
        PortfolioSettings settings,
        decimal cap)
    {
        var raw = new Dictionary<string, decimal>();
        foreach (var asset in assets)
        {
            var bars = asset.Bars;
            var lastIndex = LastIndexBefore(bars, date);
            if (lastIndex < settings.VolatilityLookback)
            {
                continue;
            }

            var history = new History(bars, lastIndex);
            if (settings.TrendFilter && (!history.Has(settings.TrendPeriod) || history.Current.Close < Indicators.Sma(history, settings.TrendPeriod)))
            {
                continue;
            }

            if (settings.Weighting == PortfolioWeighting.Equal)
            {
                raw[asset.Instrument.Symbol] = 1m;
                continue;
            }

            var returns = Enumerable.Range(0, settings.VolatilityLookback).Select(i => (double)(history[i].Close / history[i + 1].Close) - 1d).ToList();
            var mean = returns.Average();
            var vol = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1));
            if (vol > 0)
            {
                raw[asset.Instrument.Symbol] = (decimal)(1d / vol);
            }
        }

        var sum = raw.Values.Sum();
        if (sum <= 0)
        {
            return [];
        }

        // Normaliza para 100% e aplica o teto; o excedente vai para o caixa (não é redistribuído).
        return raw.ToDictionary(r => r.Key, r => Math.Round(Math.Min(cap, r.Value / sum), 4));
    }

    private static int LastIndexBefore(IReadOnlyList<Bar> bars, DateOnly date)
    {
        var lo = 0;
        var hi = bars.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (bars[mid].Date < date)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    private static decimal OpenOrLast(PortfolioAsset asset, IReadOnlyDictionary<string, Dictionary<DateOnly, int>> lookup, DateOnly date, Dictionary<string, decimal> lastClose) =>
        lookup[asset.Instrument.Symbol].TryGetValue(date, out var index) ? asset.Bars[index].Open : lastClose.GetValueOrDefault(asset.Instrument.Symbol);
}
