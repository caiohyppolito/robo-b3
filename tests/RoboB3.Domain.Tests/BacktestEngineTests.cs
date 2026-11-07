using RoboB3.Domain.Backtesting;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

public class BacktestEngineTests
{
    internal static readonly InstrumentSpec Stock = new("TEST3", AssetClass.Stock, 1, 1m, AllowShort: false);
    internal static readonly InstrumentSpec MiniIndex = new("WIN", AssetClass.Future, 1, 0.20m, AllowShort: true);
    private static readonly StrategyParameters NoParameters = new(new Dictionary<string, decimal>());

    internal static BacktestSettings Settings(
        decimal maxPositionPercent = 100m,
        decimal maxDrawdownPercent = 50m,
        bool taxes = false,
        CostModel? costs = null) =>
        new(
            initialCapital: 100_000m,
            riskPerTradePercent: 1m,
            limits: new RiskLimits(Math.Min(10m, maxDrawdownPercent), maxDrawdownPercent, maxPositionPercent, 5, 2),
            costs: costs ?? new CostModel(0, 0, 0, 0),
            riskFreeRatePercent: 0m,
            applyTaxes: taxes);

    private static BacktestResult Run(IReadOnlyList<Analysis.Bar> bars, ScriptedStrategy strategy, InstrumentSpec? instrument = null, BacktestSettings? settings = null) =>
        BacktestEngine.Run(bars, strategy, NoParameters, instrument ?? Stock, settings ?? Settings());

    [Fact]
    public void Sinal_no_fechamento_executa_na_abertura_seguinte()
    {
        var bars = TestBars.Linear(10, 100m, 1m);
        var strategy = new ScriptedStrategy((h, _) => h.Count >= 3 ? new StrategySignal(PositionDirection.Long) : StrategySignal.Flat);

        var result = Run(bars, strategy);

        var trade = Assert.Single(result.Trades);
        Assert.Equal(bars[3].Date, trade.EntryDate);
        Assert.Equal(bars[3].Open, trade.EntryPrice);
        Assert.Equal("fim do período", trade.ExitReason);
    }

    [Fact]
    public void Sem_stop_compra_ate_o_limite_de_exposicao_por_ativo()
    {
        var bars = TestBars.Linear(10, 100m, 1m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var result = Run(bars, strategy, settings: Settings(maxPositionPercent: 20m));

        Assert.Equal(200, result.Trades[0].Quantity);
    }

    [Fact]
    public void Tamanho_pelo_risco_ate_o_stop()
    {
        var bars = TestBars.Linear(10, 100m, 1m);
        var strategy = new ScriptedStrategy((h, _) => new StrategySignal(PositionDirection.Long, h.Current.Close - 5m));

        var result = Run(bars, strategy);

        // 1% de 100 mil = 1.000 de risco; 5 de distância => 200 ações.
        Assert.Equal(200, result.Trades[0].Quantity);
    }

    [Fact]
    public void Stop_com_gap_executa_na_abertura()
    {
        var bars = TestBars.FromCloses(100, 100, 100, 100);
        bars[3] = bars[3] with { Open = 80m, High = 82m, Low = 78m, Close = 81m };
        var strategy = new ScriptedStrategy((h, _) => new StrategySignal(PositionDirection.Long, h.Current.Close - 5m));

        var result = Run(bars, strategy);

        var trade = result.Trades[0];
        Assert.Equal("stop", trade.ExitReason);
        Assert.Equal(80m, trade.ExitPrice);
    }

    [Fact]
    public void Acoes_nao_operam_vendidas()
    {
        var bars = TestBars.Linear(10, 100m, -1m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Short));

        var result = Run(bars, strategy);

        Assert.Empty(result.Trades);
    }

    [Fact]
    public void Futuro_vendido_usa_multiplicador_do_contrato()
    {
        var bars = TestBars.Linear(5, 130_000m, -1_000m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Short));

        var result = Run(bars, strategy, MiniIndex);

        var trade = result.Trades[0];
        Assert.Equal(2, trade.Quantity);
        // Entra na abertura da barra 1 (130.000) e sai no fechamento da barra 4 (126.000): 4.000 pts x R$0,20 x 2.
        Assert.Equal(1_600m, trade.GrossProfit);
    }

    [Fact]
    public void Kill_switch_bloqueia_novas_entradas_mas_permite_sair()
    {
        var closes = new decimal[] { 100, 100, 95, 90, 85, 80, 78, 80, 90, 100, 110, 120 };

        // Comprado até a barra 6, sai nas barras 7-8 e tenta voltar a comprar depois.
        var strategy = new ScriptedStrategy((h, _) =>
            h.Count is 8 or 9 ? StrategySignal.Flat : new StrategySignal(PositionDirection.Long));

        var result = Run(TestBars.FromCloses(closes), strategy, settings: Settings(maxDrawdownPercent: 10m));

        Assert.Contains(result.Events, e => e.Message.StartsWith("Kill switch", StringComparison.Ordinal));
        var trade = Assert.Single(result.Trades);
        Assert.Equal("sinal", trade.ExitReason);
    }

    [Fact]
    public void Caixa_parado_rende_cdi()
    {
        var bars = TestBars.Linear(253, 100m, 0m);
        var settings = new BacktestSettings(100_000m, 1m, RiskLimits.Default, new CostModel(0, 0, 0, 0), riskFreeRatePercent: 10m, applyTaxes: false);
        var flat = new ScriptedStrategy((_, _) => StrategySignal.Flat);

        var result = BacktestEngine.Run(bars, flat, NoParameters, Stock, settings);

        Assert.Equal(result.Equity[^1].RiskFree, result.Metrics.FinalEquity, precision: 0);
        Assert.InRange(result.Metrics.TotalReturnPercent, 9.9m, 10.1m);
        Assert.True(result.Metrics.InterestEarned > 9_900m);
    }

    [Fact]
    public void Valor_investido_em_acoes_nao_rende_cdi()
    {
        var bars = TestBars.Linear(253, 100m, 0m);
        var settings = new BacktestSettings(100_000m, 1m, new RiskLimits(2m, 15m, 50m, 5, 2), new CostModel(0, 0, 0, 0), 10m, applyTaxes: false);
        var halfInvested = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var result = BacktestEngine.Run(bars, halfInvested, NoParameters, Stock, settings);

        Assert.InRange(result.Metrics.TotalReturnPercent, 4.8m, 5.2m);
    }

    [Fact]
    public void Custos_e_imposto_reduzem_o_patrimonio_final()
    {
        var bars = TestBars.Linear(60, 100m, 1m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var clean = Run(bars, strategy);
        var real = Run(bars, strategy, settings: Settings(taxes: true, costs: new CostModel(0m, 0.03m, 0m, 0m)));

        Assert.True(real.Metrics.CostsPaid > 0);
        Assert.True(real.Metrics.TaxesPaid > 0);
        Assert.True(real.Metrics.FinalEquity < clean.Metrics.FinalEquity);
        Assert.Equal(
            clean.Metrics.FinalEquity - real.Metrics.CostsPaid - real.Metrics.TaxesPaid,
            real.Metrics.FinalEquity,
            precision: 0);
    }

    [Fact]
    public void Metricas_basicas_de_uma_serie_em_alta()
    {
        var bars = TestBars.Linear(253, 100m, 0.5m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var metrics = Run(bars, strategy).Metrics;

        Assert.Equal(1, metrics.Trades);
        Assert.Equal(100m, metrics.WinRatePercent);
        Assert.True(metrics.TotalReturnPercent > 0);
        Assert.True(metrics.BenchmarkReturnPercent > 0);
        Assert.Equal(0m, metrics.RiskFreeReturnPercent);
    }

    [Fact]
    public void Inicio_fora_da_amostra_nao_opera_antes_do_indice()
    {
        var bars = TestBars.Linear(30, 100m, 1m);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var result = BacktestEngine.Run(bars, strategy, NoParameters, Stock, Settings(), startIndex: 20);

        Assert.Equal(bars[21].Date, result.Trades[0].EntryDate);
        Assert.Equal(bars[20].Date, result.Equity[0].Date);
    }

    [Fact]
    public void Historico_insuficiente_lanca_erro_de_dominio()
    {
        var strategy = new ScriptedStrategy((_, _) => StrategySignal.Flat, warmup: 50);

        Assert.Throws<Common.DomainException>(() => Run(TestBars.Linear(10, 100m, 1m), strategy));
    }

    [Fact]
    public void Operacao_registra_excursoes_pela_maxima_e_minima_das_barras()
    {
        var bars = TestBars.FromCloses(100, 100, 104, 97, 102);
        var strategy = new ScriptedStrategy((_, _) => new StrategySignal(PositionDirection.Long));

        var trade = Assert.Single(Run(bars, strategy).Trades);

        // Entrada a 100 (abertura da barra 1); máxima 104 x 1,01 = 105,04; mínima 97 x 0,99 = 96,03.
        Assert.Equal(5.04m, trade.MfePercent);
        Assert.Equal(3.97m, trade.MaePercent);
    }
}
