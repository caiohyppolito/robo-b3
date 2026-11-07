namespace RoboB3.Domain.Backtesting;

public sealed record PerformanceMetrics(
    decimal InitialCapital,
    decimal FinalEquity,
    decimal TotalReturnPercent,
    decimal CagrPercent,
    decimal MaxDrawdownPercent,
    decimal VolatilityPercent,
    decimal Sharpe,
    int Trades,
    decimal WinRatePercent,
    decimal ProfitFactor,
    decimal AverageTradePercent,
    decimal ExposurePercent,
    decimal CostsPaid,
    decimal TaxesPaid,
    decimal InterestEarned,
    decimal BenchmarkReturnPercent,
    decimal BenchmarkMaxDrawdownPercent,
    decimal RiskFreeReturnPercent,
    int SignalsFiltered = 0,
    decimal Sortino = 0,
    decimal Calmar = 0,
    decimal CVaR95Percent = 0,
    decimal ExpectancyPerTrade = 0,
    decimal ExpectancyMargin95 = 0,
    decimal TurnoverPerYear = 0,
    int AmbiguousTrades = 0)
{
    public const int TradingDaysPerYear = 252;
    private const decimal MaxProfitFactor = 999m;

    /// <param name="equity">Um ponto por pregão (o motor já reamostra barras intradiárias), para o Sharpe ser comparável.</param>
    public static PerformanceMetrics Calculate(
        IReadOnlyList<EquityPoint> equity,
        IReadOnlyList<BacktestTrade> trades,
        int barsInPosition,
        int totalBars,
        decimal costsPaid,
        decimal taxesPaid,
        decimal interestEarned,
        decimal riskFreeRatePercent)
    {
        ArgumentNullException.ThrowIfNull(equity);
        ArgumentNullException.ThrowIfNull(trades);
        if (equity.Count == 0)
        {
            throw new ArgumentException("Curva de patrimônio vazia.", nameof(equity));
        }

        var initial = equity[0].Equity;
        var final = equity[^1].Equity;
        var years = Math.Max(1, equity[^1].Date.DayNumber - equity[0].Date.DayNumber) / 365.25;
        var totalReturn = (final / initial) - 1m;
        var cagr = final <= 0 ? -1d : Math.Pow((double)(final / initial), 1d / years) - 1d;

        var dailyRiskFree = Math.Pow(1d + ((double)riskFreeRatePercent / 100d), 1d / TradingDaysPerYear) - 1d;
        var returns = new List<double>(equity.Count);
        for (var i = 1; i < equity.Count; i++)
        {
            returns.Add(equity[i - 1].Equity == 0 ? 0 : (double)((equity[i].Equity / equity[i - 1].Equity) - 1m));
        }

        var mean = returns.Count > 0 ? returns.Average() : 0d;
        var stdDev = returns.Count > 1 ? Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1)) : 0d;
        var sharpe = stdDev > 0 ? (mean - dailyRiskFree) / stdDev * Math.Sqrt(TradingDaysPerYear) : 0d;

        // Sortino: só as quedas abaixo do CDI contam como risco.
        var downside = returns.Count > 1 ? Math.Sqrt(returns.Sum(r => Math.Pow(Math.Min(0, r - dailyRiskFree), 2)) / (returns.Count - 1)) : 0d;
        var sortino = downside > 0 ? (mean - dailyRiskFree) / downside * Math.Sqrt(TradingDaysPerYear) : 0d;

        // CVaR 95%: média dos 5% piores dias.
        var worst = returns.Order().Take(Math.Max(1, returns.Count / 20)).ToList();
        var cvar = worst.Count > 0 ? -worst.Average() : 0d;

        // Expectativa líquida por operação com margem de erro (95%, aproximação normal): mostra o tamanho da incerteza.
        var nets = trades.Select(t => (double)t.NetProfit).ToList();
        var expectancy = nets.Count > 0 ? nets.Average() : 0d;
        var netSd = nets.Count > 1 ? Math.Sqrt(nets.Sum(x => (x - expectancy) * (x - expectancy)) / (nets.Count - 1)) : 0d;
        var margin = nets.Count > 1 ? 1.96 * netSd / Math.Sqrt(nets.Count) : 0d;

        var maxDrawdown = MaxDrawdown(equity.Select(e => e.Equity));
        var calmar = maxDrawdown > 0 ? cagr / (double)maxDrawdown : 0d;
        var traded = trades.Sum(t => t.EntryPrice * t.Quantity);
        var turnover = initial > 0 ? traded / initial / (decimal)years : 0m;

        var wins = trades.Where(t => t.NetProfit > 0).Sum(t => t.NetProfit);
        var losses = -trades.Where(t => t.NetProfit < 0).Sum(t => t.NetProfit);
        var profitFactor = losses > 0 ? Math.Min(MaxProfitFactor, wins / losses) : wins > 0 ? MaxProfitFactor : 0m;

        return new PerformanceMetrics(
            InitialCapital: initial,
            FinalEquity: Math.Round(final, 2),
            TotalReturnPercent: Pct(totalReturn),
            CagrPercent: Pct((decimal)cagr),
            MaxDrawdownPercent: Pct(MaxDrawdown(equity.Select(e => e.Equity))),
            VolatilityPercent: Pct((decimal)(stdDev * Math.Sqrt(TradingDaysPerYear))),
            Sharpe: Math.Round((decimal)sharpe, 2),
            Trades: trades.Count,
            WinRatePercent: trades.Count == 0 ? 0 : Pct((decimal)trades.Count(t => t.NetProfit > 0) / trades.Count),
            ProfitFactor: Math.Round(profitFactor, 2),
            AverageTradePercent: trades.Count == 0 ? 0 : Math.Round(trades.Average(t => t.ReturnPercent), 2),
            ExposurePercent: totalBars == 0 ? 0 : Pct((decimal)barsInPosition / totalBars),
            CostsPaid: Math.Round(costsPaid, 2),
            TaxesPaid: Math.Round(taxesPaid, 2),
            InterestEarned: Math.Round(interestEarned, 2),
            BenchmarkReturnPercent: Pct((equity[^1].Benchmark / equity[0].Benchmark) - 1m),
            BenchmarkMaxDrawdownPercent: Pct(MaxDrawdown(equity.Select(e => e.Benchmark))),
            RiskFreeReturnPercent: Pct((equity[^1].RiskFree / equity[0].RiskFree) - 1m),
            Sortino: Math.Round((decimal)sortino, 2),
            Calmar: Math.Round((decimal)calmar, 2),
            CVaR95Percent: Pct((decimal)cvar),
            ExpectancyPerTrade: Math.Round((decimal)expectancy, 2),
            ExpectancyMargin95: Math.Round((decimal)margin, 2),
            TurnoverPerYear: Math.Round(turnover, 2),
            AmbiguousTrades: trades.Count(t => t.AmbiguousBar));
    }

    public static decimal MaxDrawdown(IEnumerable<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        decimal peak = 0, maxDrawdown = 0;
        foreach (var value in values)
        {
            peak = Math.Max(peak, value);
            if (peak > 0)
            {
                maxDrawdown = Math.Max(maxDrawdown, (peak - value) / peak);
            }
        }

        return maxDrawdown;
    }

    private static decimal Pct(decimal fraction) => Math.Round(fraction * 100m, 2);
}
