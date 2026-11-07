using RoboB3.Domain.Analysis;

namespace RoboB3.Domain.Tests;

public class IndicatorsTests
{
    [Fact]
    public void Historico_nao_permite_acessar_barras_futuras()
    {
        var bars = TestBars.Linear(10, 10m, 1m);
        var history = new History(bars, 4);

        Assert.Equal(5, history.Count);
        Assert.Equal(14m, history.Current.Close);
        Assert.Throws<ArgumentOutOfRangeException>(() => history[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => history[5]);
    }

    [Fact]
    public void Sma_usa_as_ultimas_barras_com_deslocamento()
    {
        var history = new History(TestBars.FromCloses(1, 2, 3, 4, 5), 4);

        Assert.Equal(4m, Indicators.Sma(history, 3));
        Assert.Equal(3m, Indicators.Sma(history, 3, offset: 1));
    }

    [Fact]
    public void Rsi_e_100_so_com_altas_e_0_so_com_quedas()
    {
        Assert.Equal(100m, Indicators.Rsi(new History(TestBars.Linear(10, 10, 1), 9), 5));
        Assert.Equal(0m, Indicators.Rsi(new History(TestBars.Linear(10, 20, -1), 9), 5));
    }

    [Fact]
    public void Rsi_equilibrado_e_50()
    {
        var history = new History(TestBars.FromCloses(10, 11, 10, 11, 10), 4);

        Assert.Equal(50m, Indicators.Rsi(history, 4));
    }

    [Fact]
    public void Canal_ignora_a_barra_atual()
    {
        var bars = TestBars.FromCloses(10, 12, 11, 20);
        var history = new History(bars, 3);

        Assert.Equal(bars[1].High, Indicators.HighestHigh(history, 3));
        Assert.Equal(bars[0].Low, Indicators.LowestLow(history, 3));
    }

    [Fact]
    public void Indicador_com_historico_insuficiente_lanca_erro()
    {
        var history = new History(TestBars.Linear(5, 10, 1), 4);

        Assert.Throws<InvalidOperationException>(() => Indicators.Sma(history, 6));
    }

    [Fact]
    public void Barras_aplicam_ajuste_de_proventos()
    {
        var candle = new MarketData.Candle("PETR4", MarketData.Timeframe.D1, DateTimeOffset.UnixEpoch, 20m, 22m, 19m, 21m, 100, adjustedClose: 10.5m);

        var bar = Bar.FromCandles([candle]).Single();

        Assert.Equal(10m, bar.Open);
        Assert.Equal(11m, bar.High);
        Assert.Equal(10.5m, bar.Close);
    }
}
