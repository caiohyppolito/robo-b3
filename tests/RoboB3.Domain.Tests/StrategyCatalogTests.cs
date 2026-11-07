using RoboB3.Domain.Common;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

public class StrategyCatalogTests
{
    [Fact]
    public void Normaliza_completando_parametros_ausentes_com_padrao()
    {
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);

        var parameters = definition.Normalize(new Dictionary<string, decimal> { ["fast"] = 10 });

        Assert.Equal(10m, parameters.Get("fast"));
        Assert.Equal(100m, parameters.Get("slow"));
    }

    [Fact]
    public void Rejeita_parametro_fora_da_faixa_ou_desconhecido()
    {
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);

        Assert.Throws<DomainException>(() => definition.Normalize(new Dictionary<string, decimal> { ["fast"] = 1 }));
        Assert.Throws<DomainException>(() => definition.Normalize(new Dictionary<string, decimal> { ["xyz"] = 1 }));
    }

    [Fact]
    public void Grade_gera_todas_as_combinacoes()
    {
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);

        Assert.Equal(3 * 2 * 2 * 2, definition.Grid().Count());
        Assert.Single(StrategyCatalog.Get(StrategyKeys.BuyAndHold).Grid());
    }

    [Fact]
    public void Estrategia_inexistente_lanca_erro()
    {
        Assert.Throws<DomainException>(() => StrategyCatalog.Get("nao-existe"));
    }

    [Fact]
    public void Todas_as_estrategias_rodam_sem_erro_com_parametros_padrao()
    {
        var bars = TestBars.FromCloses(Enumerable.Range(0, 400)
            .Select(i => 50m + (10m * (decimal)Math.Sin(i / 15d)) + (i * 0.05m))
            .ToArray());

        foreach (var definition in StrategyCatalog.All)
        {
            var parameters = definition.DefaultParameters();
            var result = Backtesting.BacktestEngine.Run(
                bars, definition.Create(parameters), parameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings());
            Assert.NotEmpty(result.Equity);
        }
    }
}
