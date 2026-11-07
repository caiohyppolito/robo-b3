using RoboB3.Domain.Backtesting;
using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Tests;

public class InstrumentSpecTests
{
    private static InstrumentSpec Spec(string symbol, AssetClass assetClass = AssetClass.Future) =>
        new(symbol, assetClass, 1, 1m, true);

    [Theory]
    [InlineData("WINZ26", 5)]
    [InlineData("WIN", 5)]
    [InlineData("WDOZ26", 0.5)]
    [InlineData("WDO", 0.5)]
    public void Tick_dos_futuros_segue_a_variacao_minima_da_b3(string symbol, decimal tick) =>
        Assert.Equal(tick, Spec(symbol).TickSize);

    [Fact]
    public void Acao_anda_de_um_centavo() =>
        Assert.Equal(0.01m, Spec("PETR4", AssetClass.Stock).TickSize);

    [Theory]
    [InlineData(129_985.2, 129_985)]
    [InlineData(129_987.5, 129_990)]
    [InlineData(129_982.4, 129_980)]
    [InlineData(129_980, 129_980)]
    public void Stop_do_mini_indice_e_arredondado_para_multiplo_de_cinco(decimal bruto, decimal esperado)
    {
        var arredondado = Spec("WINZ26").RoundToTick(bruto);

        Assert.Equal(esperado, arredondado);
        Assert.Equal(0m, arredondado % 5m);
    }

    [Theory]
    [InlineData(5_432.3, 5_432.5)]
    [InlineData(5_432.24, 5_432.0)]
    public void Stop_do_mini_dolar_e_arredondado_para_meio_ponto(decimal bruto, decimal esperado) =>
        Assert.Equal(esperado, Spec("WDOZ26").RoundToTick(bruto));

    [Fact]
    public void Stop_de_acao_e_arredondado_para_centavo() =>
        Assert.Equal(45.39m, Spec("PETR4", AssetClass.Stock).RoundToTick(45.3871m));
}
