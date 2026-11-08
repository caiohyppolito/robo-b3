using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Tests;

public class PortfolioBacktestTests
{
    private static PortfolioAsset Asset(string symbol, Func<int, decimal> close, int count = 400) =>
        new(new InstrumentSpec(symbol, AssetClass.Stock, 1, 1m, false), TestBars.FromCloses(Enumerable.Range(0, count).Select(close).ToArray()));

    private static PortfolioSettings Settings(decimal cap = 100m, PortfolioWeighting weighting = PortfolioWeighting.Equal, bool trend = false, CostModel? costs = null) =>
        new(100_000m, weighting, cap, trend, costs ?? new CostModel(0, 0, 0, 0), 0m);

    private static DateOnly From(PortfolioAsset a) => a.Bars[250].Date;

    [Fact]
    public void Teto_por_ativo_deixa_o_excedente_no_caixa()
    {
        var assets = new[] { Asset("AAA3", _ => 10m), Asset("BBB3", _ => 20m), Asset("CCC3", _ => 40m) };

        var result = PortfolioBacktest.Run(assets, Settings(cap: 20m), From(assets[0]));

        var first = result.Rebalances[0];
        Assert.All(first.TargetWeights.Values, w => Assert.Equal(0.2m, w));
        Assert.Equal(0.4m, first.CashWeight);
        Assert.Equal(100_000m, result.Equity[0].Equity);
        Assert.InRange(result.AverageInvestedPercent, 59m, 61m);
    }

    [Fact]
    public void Inverso_da_volatilidade_da_mais_peso_ao_ativo_mais_calmo()
    {
        var calm = Asset("CALM3", i => 50m + (i % 2 == 0 ? 0.1m : -0.1m));
        var wild = Asset("WILD3", i => 50m + (i % 2 == 0 ? 3m : -3m));

        var result = PortfolioBacktest.Run([calm, wild], Settings(weighting: PortfolioWeighting.InverseVolatility), From(calm));

        var weights = result.Rebalances[0].TargetWeights;
        Assert.True(weights["CALM3"] > 0.9m);
        Assert.True(weights["WILD3"] < 0.1m);
    }

    [Fact]
    public void Filtro_de_tendencia_tira_o_ativo_em_queda()
    {
        var up = Asset("UP3", i => 10m + (i * 0.05m));
        var down = Asset("DOWN3", i => 50m - (i * 0.05m));

        var result = PortfolioBacktest.Run([up, down], Settings(trend: true), From(up));

        var weights = result.Rebalances[0].TargetWeights;
        Assert.True(weights.ContainsKey("UP3"));
        Assert.False(weights.ContainsKey("DOWN3"));
        Assert.Equal(0m, result.FinalWeights["DOWN3"]);
    }

    [Fact]
    public void Custos_de_rebalanceamento_entram_no_resultado()
    {
        var assets = new[] { Asset("AAA3", _ => 10m), Asset("BBB3", _ => 20m) };

        var free = PortfolioBacktest.Run(assets, Settings(), From(assets[0]));
        var paid = PortfolioBacktest.Run(assets, Settings(costs: new CostModel(10m, 0.05m, 0.1m, 0)), From(assets[0]));

        Assert.True(paid.Metrics.CostsPaid > 0);
        Assert.True(paid.Equity[^1].Equity < free.Equity[^1].Equity);
    }

    [Fact]
    public void Pesos_usam_so_dados_ate_o_pregao_anterior()
    {
        // Sem 60 pregões antes da data inicial, não há volatilidade conhecida: tudo fica no caixa.
        var assets = new[] { Asset("AAA3", i => 10m + i) };

        var result = PortfolioBacktest.Run(assets, Settings(weighting: PortfolioWeighting.InverseVolatility), assets[0].Bars[30].Date);

        Assert.Empty(result.Rebalances[0].TargetWeights);
        Assert.Equal(1m, result.Rebalances[0].CashWeight);
    }

    [Fact]
    public void Futuros_nao_entram_na_carteira()
    {
        var future = new PortfolioAsset(new InstrumentSpec("WIN", AssetClass.Future, 1, 0.2m, true), TestBars.Linear(300, 100m, 1m));

        Assert.Throws<DomainException>(() => PortfolioBacktest.Run([future], Settings(), future.Bars[0].Date));
    }
}

public class PortfolioLotTests
{
    [Fact]
    public void Lote_padrao_caro_nao_cabe_mas_o_fracionario_compra()
    {
        var expensive = new PortfolioAsset(new InstrumentSpec("CARO3", AssetClass.Stock, 100, 1m, false),
            TestBars.FromCloses(Enumerable.Repeat(600m, 300).ToArray()));
        var settings = new PortfolioSettings(100_000m, PortfolioWeighting.Equal, 20m, false, new CostModel(0, 0, 0, 0), 0m);

        var standard = PortfolioBacktest.Run([expensive], settings with { Fractional = false }, expensive.Bars[100].Date);
        var fractional = PortfolioBacktest.Run([expensive], settings, expensive.Bars[100].Date);

        Assert.Equal(["CARO3"], standard.Rebalances[0].NotBought);
        Assert.Equal(0m, standard.FinalWeights["CARO3"]);
        Assert.Empty(fractional.Rebalances[0].NotBought);
        Assert.Equal(0.198m, fractional.FinalWeights["CARO3"]);
    }
}
