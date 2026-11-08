using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

public class RiskFreeCurveTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    [Fact]
    public void Usa_a_taxa_do_dia_a_ultima_conhecida_ou_a_fixa_antes_da_serie()
    {
        var curve = new RiskFreeCurve(
            [new(Day, 0.05m), new(Day.AddDays(2), 0.04m)],
            fallbackAnnualPercent: 10m);

        Assert.Equal(0.0005m, curve.DailyRate(Day));
        Assert.Equal(0.0005m, curve.DailyRate(Day.AddDays(1)));
        Assert.Equal(0.0004m, curve.DailyRate(Day.AddDays(5)));
        Assert.Equal(RiskFreeCurve.Constant(10m), curve.DailyRate(Day.AddDays(-1)));
    }

    [Fact]
    public void Backtest_rende_o_cdi_real_de_cada_dia()
    {
        var bars = TestBars.Linear(4, 100m, 0m);
        var rates = bars.Select((b, i) => new KeyValuePair<DateOnly, decimal>(b.Date, i == 0 ? 0.1m : 0.05m));
        var settings = new BacktestSettings(100_000m, 1m, BacktestEngineTests.Settings().Limits, new CostModel(0, 0, 0, 0), 0m,
            applyTaxes: false, riskFreeCurve: new RiskFreeCurve(rates, 0m));
        var flat = new ScriptedStrategy((_, _) => StrategySignal.Flat);

        var result = BacktestEngine.Run(bars, flat, new StrategyParameters(new Dictionary<string, decimal>()), BacktestEngineTests.Stock, settings);

        // Rende 0,1% no primeiro dia e 0,05% nos dois seguintes.
        var expected = 100_000m * 1.001m * 1.0005m * 1.0005m;
        Assert.Equal(Math.Round(expected, 2), result.Equity[^1].RiskFree);
        Assert.Equal(Math.Round(expected, 2), result.Metrics.FinalEquity, precision: 1);
    }
}
