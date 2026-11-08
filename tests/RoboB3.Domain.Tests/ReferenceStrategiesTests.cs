using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

/// <summary>Indicadores, estratégia e regras de alvo trazidos da análise das referências.</summary>
public class ReferenceStrategiesTests
{
    private static readonly StrategyParameters NoParameters = new(new Dictionary<string, decimal>());

    private static List<Bar> Intraday(DateOnly date, params decimal[] closes)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < closes.Length; i++)
        {
            var open = i == 0 ? closes[0] : closes[i - 1];
            var time = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-3)).AddMinutes(5 * i);
            bars.Add(new Bar(date, open, Math.Max(open, closes[i]) + 5, Math.Min(open, closes[i]) - 5, closes[i], 100) { Time = time });
        }

        return bars;
    }

    [Fact]
    public void Ema_de_serie_constante_e_o_proprio_valor()
    {
        var history = new History(TestBars.FromCloses(Enumerable.Repeat(50m, 30).ToArray()), 29);

        Assert.Equal(50m, Indicators.Ema(history, 9), precision: 10);
    }

    [Fact]
    public void Ema_reage_mais_rapido_que_a_media_simples_a_um_salto()
    {
        var closes = Enumerable.Repeat(100m, 30).Append(110m).ToArray();
        var history = new History(TestBars.FromCloses(closes), 30);

        Assert.Equal(100m + (10m * 2m / 11m), Indicators.Ema(history, 10), precision: 8);
        Assert.Equal(101m, Indicators.Sma(history, 10));
    }

    [Fact]
    public void Vwap_usa_so_as_barras_do_pregao_atual()
    {
        var day1 = new DateOnly(2026, 3, 2);
        var day2 = day1.AddDays(1);
        var bars = new List<Bar>
        {
            new(day1, 1000, 1000, 1000, 1000, 1000),
            new(day2, 10, 12, 8, 10, 100),
            new(day2, 20, 22, 18, 20, 300),
        };

        var vwap = Indicators.SessionVwap(new History(bars, 2));

        Assert.Equal(17.5m, vwap);
    }

    [Fact]
    public void Adx_alto_em_tendencia_e_baixo_em_mercado_lateral()
    {
        var trend = new History(TestBars.Linear(60, 100m, 1m), 59);
        var choppy = new History(TestBars.FromCloses(Enumerable.Range(0, 60).Select(i => 100m + (decimal)(5 * Math.Sin(i * 0.9))).ToArray()), 59);

        Assert.True(Indicators.Adx(trend, 14) > 50m);
        Assert.True(Indicators.Adx(choppy, 14) < 25m);
    }

    [Fact]
    public void Ema_vwap_compra_no_cruzamento_acima_da_vwap_com_stop_e_alvo()
    {
        var closes = Enumerable.Repeat(100m, 25).Append(110m).ToArray();
        var bars = Intraday(new DateOnly(2026, 3, 2), closes);
        var strategy = new EmaVwapStrategy(9, 21, 100m, 200m, 915, 1700);

        var signal = strategy.Evaluate(new History(bars, bars.Count - 1), PositionContext.None);

        Assert.Equal(PositionDirection.Long, signal.Target);
        Assert.Equal(10m, signal.StopPrice);
        Assert.Equal(310m, signal.TargetPrice);
    }

    [Fact]
    public void Ema_vwap_nao_entra_fora_da_janela_nem_em_barras_diarias()
    {
        var closes = Enumerable.Repeat(100m, 25).Append(110m).ToArray();
        var intraday = Intraday(new DateOnly(2026, 3, 2), closes);
        var late = new EmaVwapStrategy(9, 21, 100m, 200m, 1300, 1700);
        var daily = TestBars.FromCloses(closes);
        var strategy = new EmaVwapStrategy(9, 21, 100m, 200m, 915, 1700);

        Assert.Equal(PositionDirection.Flat, late.Evaluate(new History(intraday, intraday.Count - 1), PositionContext.None).Target);
        Assert.Equal(PositionDirection.Flat, strategy.Evaluate(new History(daily, daily.Count - 1), PositionContext.None).Target);
    }

    private static ScriptedStrategy EntryAtThirdBar(decimal stopDistance, decimal targetDistance) =>
        new((h, p) =>
        {
            if (p.Direction != PositionDirection.Flat)
            {
                return new StrategySignal(p.Direction);
            }

            return h.Count == 3
                ? new StrategySignal(PositionDirection.Long, h.Current.Close - stopDistance, h.Current.Close + targetDistance)
                : StrategySignal.Flat;
        });

    [Fact]
    public void Alvo_atingido_encerra_no_preco_do_alvo()
    {
        var bars = TestBars.Linear(10, 100m, 1m);

        var result = BacktestEngine.Run(bars, EntryAtThirdBar(50m, 3m), NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());

        var trade = result.Trades[0];
        Assert.Equal(102m, trade.EntryPrice);
        Assert.Equal(105m, trade.ExitPrice);
        Assert.Equal("alvo", trade.ExitReason);
        Assert.False(trade.AmbiguousBar);
    }

    [Fact]
    public void Stop_e_alvo_na_mesma_barra_assume_o_stop()
    {
        var bars = TestBars.Linear(10, 100m, 1m);
        bars[4] = bars[4] with { High = 110m, Low = 90m };

        var result = BacktestEngine.Run(bars, EntryAtThirdBar(5m, 3m), NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());

        var trade = result.Trades[0];
        Assert.Equal(97m, trade.ExitPrice);
        Assert.Equal("stop (barra ambígua)", trade.ExitReason);
        Assert.True(trade.AmbiguousBar);
        Assert.Equal(1, result.Metrics.AmbiguousTrades);
    }

    [Fact]
    public void Metricas_trazem_expectativa_com_margem_e_risco_de_cauda()
    {
        var closes = Enumerable.Range(0, 120).Select(i => 100m + (i % 10 < 6 ? i % 10 : 10 - (i % 10))).ToArray();
        var bars = TestBars.FromCloses(closes);
        var swing = new ScriptedStrategy((h, _) => h.Count % 10 < 5 ? new StrategySignal(PositionDirection.Long) : StrategySignal.Flat);

        var metrics = BacktestEngine.Run(bars, swing, NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings()).Metrics;

        Assert.True(metrics.Trades > 5);
        Assert.True(metrics.ExpectancyMargin95 > 0);
        Assert.True(metrics.CVaR95Percent > 0, "CVaR é a perda média dos 5% piores dias, em valor positivo");
        Assert.True(metrics.TurnoverPerYear > 0);
    }
}

public class EntryStopTests
{
    private static readonly StrategyParameters NoParameters = new(new Dictionary<string, decimal>());

    private static ScriptedStrategy EntryStopAtThirdBar(decimal level, decimal stop, decimal? target = null) =>
        new((h, p) =>
        {
            if (p.Direction != PositionDirection.Flat)
            {
                return new StrategySignal(p.Direction);
            }

            return h.Count == 3 ? new StrategySignal(PositionDirection.Long, stop, target, level) : StrategySignal.Flat;
        });

    [Fact]
    public void Stop_de_entrada_executa_no_nivel_quando_a_barra_seguinte_toca()
    {
        // Barra 3: abre 102, máxima 103,03.
        var bars = TestBars.Linear(10, 100m, 1m);

        var result = BacktestEngine.Run(bars, EntryStopAtThirdBar(102.5m, 100m, 107.5m), NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());

        var trade = result.Trades[0];
        Assert.Equal(bars[3].Date, trade.EntryDate);
        Assert.Equal(102.5m, trade.EntryPrice);
        Assert.Equal("alvo", trade.ExitReason);
        Assert.Equal(107.5m, trade.ExitPrice);
    }

    [Fact]
    public void Stop_de_entrada_nao_tocado_expira_sem_operacao()
    {
        var bars = TestBars.Linear(10, 100m, 1m);

        var result = BacktestEngine.Run(bars, EntryStopAtThirdBar(110m, 100m), NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());

        Assert.Empty(result.Trades);
    }

    [Fact]
    public void Gap_acima_do_nivel_executa_na_abertura()
    {
        var bars = TestBars.Linear(10, 100m, 1m);
        bars[3] = bars[3] with { Open = 104m, High = 105m, Low = 103.5m };

        var result = BacktestEngine.Run(bars, EntryStopAtThirdBar(102.5m, 100m), NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());

        Assert.Equal(104m, result.Trades[0].EntryPrice);
    }

    [Fact]
    public void Dunnigan_arma_compra_acima_da_maxima_depois_de_correcao_na_alta()
    {
        // Alta longa, depois três barras com máximas e mínimas menores.
        var closes = Enumerable.Range(0, 60).Select(i => 100m + i).Concat([158m, 157m, 156m, 155m]).ToArray();
        var bars = TestBars.FromCloses(closes);
        var strategy = new DunniganPullbackStrategy(3, 50, 0m, 2m);

        var signal = strategy.Evaluate(new History(bars, bars.Count - 1), PositionContext.None);

        Assert.Equal(PositionDirection.Long, signal.Target);
        Assert.Equal(bars[^1].High, signal.EntryStop);
        Assert.Equal(bars[^1].Low, signal.StopPrice);
        Assert.Equal(signal.EntryStop + (2 * (signal.EntryStop - signal.StopPrice)), signal.TargetPrice);
    }

    [Fact]
    public void Dunnigan_nao_arma_sem_correcao()
    {
        var bars = TestBars.Linear(70, 100m, 1m);

        var signal = new DunniganPullbackStrategy(3, 50, 0.1m, 2m).Evaluate(new History(bars, bars.Count - 1), PositionContext.None);

        Assert.Equal(PositionDirection.Flat, signal.Target);
    }
}
