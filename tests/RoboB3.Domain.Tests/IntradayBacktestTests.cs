using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Tests;

public class IntradayBacktestTests
{
    private static readonly StrategyParameters NoParameters = new(new Dictionary<string, decimal>());

    /// <summary>Pregões de 10 barras de 5 minutos, a partir das 10h de Brasília.</summary>
    private static List<Bar> Sessions(int days, int barsPerDay, Func<int, decimal> close)
    {
        var bars = new List<Bar>();
        var index = 0;
        for (var d = 0; d < days; d++)
        {
            var date = new DateOnly(2026, 3, 2).AddDays(d);
            for (var b = 0; b < barsPerDay; b++)
            {
                var c = close(index);
                var open = index == 0 ? c : bars[^1].Close;
                var time = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-3)).AddMinutes(5 * b);
                bars.Add(new Bar(date, open, Math.Max(open, c) + 5, Math.Min(open, c) - 5, c, 100) { Time = time });
                index++;
            }
        }

        return bars;
    }

    private static BacktestSettings Settings(bool flatten, decimal riskFree = 0m) =>
        new(100_000m, 1m, new RiskLimits(10m, 50m, 100m, 5, 2), new CostModel(0, 0, 0, 0), riskFree, applyTaxes: false, flattenAtDayEnd: flatten);

    [Fact]
    public void Day_trade_zera_na_ultima_barra_de_cada_pregao()
    {
        var bars = Sessions(3, 10, i => 130_000m + (i * 10));
        var alwaysLong = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var result = BacktestEngine.Run(bars, alwaysLong, NoParameters, BacktestEngineTests.MiniIndex, Settings(flatten: true));

        Assert.All(result.Trades, t => Assert.Equal(t.EntryDate, t.ExitDate));
        Assert.Equal(3, result.Trades.Count);
        Assert.Equal(2, result.Trades.Count(t => t.ExitReason == "fim do pregão"));
        Assert.All(result.Trades, t => Assert.NotNull(t.EntryTime));
    }

    [Fact]
    public void Sem_zerar_a_posicao_atravessa_os_pregoes()
    {
        var bars = Sessions(3, 10, i => 130_000m + (i * 10));
        var alwaysLong = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var result = BacktestEngine.Run(bars, alwaysLong, NoParameters, BacktestEngineTests.MiniIndex, Settings(flatten: false));

        var trade = Assert.Single(result.Trades);
        Assert.NotEqual(trade.EntryDate, trade.ExitDate);
    }

    [Fact]
    public void Curva_de_patrimonio_e_cdi_tem_um_ponto_por_pregao()
    {
        var bars = Sessions(5, 12, i => 5_000m);
        var flat = new ScriptedStrategy((_, _) => StrategySignal.Flat);

        var result = BacktestEngine.Run(bars, flat, NoParameters, BacktestEngineTests.MiniIndex, Settings(false, riskFree: 10m));

        Assert.Equal(5, result.Equity.Count);
        var dailyRate = Math.Pow(1.10, 1d / 252) - 1;
        Assert.Equal(100_000m * (decimal)Math.Pow(1 + dailyRate, 4), result.Equity[^1].RiskFree, precision: 0);
        Assert.Equal(result.Equity[^1].RiskFree, result.Metrics.FinalEquity, precision: 0);
        Assert.Equal(0m, result.Metrics.ExposurePercent);
    }

    [Fact]
    public void Perda_diaria_e_medida_contra_a_abertura_do_pregao()
    {
        // Queda contínua dentro do mesmo pregão: barra a barra é pequena, no acumulado do dia estoura 2%.
        var bars = Sessions(2, 20, i => 130_000m - (i * 400));
        var alwaysLong = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));
        var settings = new BacktestSettings(100_000m, 1m, new RiskLimits(2m, 50m, 100m, 5, 2), new CostModel(0, 0, 0, 0), 0m, applyTaxes: false);

        var result = BacktestEngine.Run(bars, alwaysLong, NoParameters, BacktestEngineTests.MiniIndex, settings);

        Assert.Contains(result.Events, e => e.Message.StartsWith("Perda diária", StringComparison.Ordinal));
    }

    [Fact]
    public void Barras_intradiarias_usam_a_data_do_pregao_em_brasilia()
    {
        // 23h30 UTC de 2 de março = 20h30 em Brasília, ainda 2 de março.
        var late = new Candle("WIN", Timeframe.M5, new DateTimeOffset(2026, 3, 2, 23, 30, 0, TimeSpan.Zero), 10m, 11m, 9m, 10m, 1);
        var daily = new Candle("PETR4", Timeframe.D1, new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), 10m, 11m, 9m, 10m, 1);

        var intradayBar = Bar.FromCandles([late]).Single();
        var dailyBar = Bar.FromCandles([daily]).Single();

        Assert.Equal(new DateOnly(2026, 3, 2), intradayBar.Date);
        Assert.NotNull(intradayBar.Time);
        Assert.Equal(new DateOnly(2026, 3, 2), dailyBar.Date);
        Assert.Null(dailyBar.Time);
    }

    [Theory]
    [InlineData(100, 5.0, 200)]    // 1% de 100 mil / 5 de stop = 200 ações
    [InlineData(100, null, 1000)]  // sem stop: limitado a 100% de exposição
    public void Tamanho_da_posicao_em_acoes(decimal price, double? stop, int expected)
    {
        var size = PositionSizer.Size(price, (decimal?)stop, 100_000m, 1m, BacktestEngineTests.Stock, new RiskLimits(2, 15, 100, 5, 2));

        Assert.Equal(expected, size);
    }

    [Fact]
    public void Tamanho_em_futuros_respeita_contratos_ja_abertos()
    {
        var limits = new RiskLimits(2, 15, 100, 5, maxFuturesContracts: 3);

        Assert.Equal(3, PositionSizer.Size(130_000m, 100m, 100_000m, 1m, BacktestEngineTests.MiniIndex, limits));
        Assert.Equal(1, PositionSizer.Size(130_000m, 100m, 100_000m, 1m, BacktestEngineTests.MiniIndex, limits, contractsInOtherPositions: 2));
        Assert.Equal(0, PositionSizer.Size(130_000m, 100m, 100_000m, 1m, BacktestEngineTests.MiniIndex, limits, contractsInOtherPositions: 5));
    }
}
