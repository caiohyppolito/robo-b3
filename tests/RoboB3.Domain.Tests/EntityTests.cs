using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Domain.Tests;

public class EntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Candle_rejeita_maxima_abaixo_do_fechamento()
    {
        Assert.Throws<DomainException>(() => new Candle("PETR4", Timeframe.D1, Now, 10m, 10.5m, 9m, 11m, 1000));
    }

    [Fact]
    public void Candle_normaliza_simbolo_e_horario_para_utc()
    {
        var local = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(-3));

        var candle = new Candle("petr4", Timeframe.D1, local, 10m, 11m, 9m, 10.5m, 1000);

        Assert.Equal("PETR4", candle.Symbol);
        Assert.Equal(TimeSpan.Zero, candle.OpenTime.Offset);
        Assert.Equal(13, candle.OpenTime.Hour);
    }

    [Fact]
    public void Candle_so_atualiza_com_mesma_chave()
    {
        var candle = new Candle("PETR4", Timeframe.D1, Now, 10m, 11m, 9m, 10.5m, 1000);
        var other = new Candle("VALE3", Timeframe.D1, Now, 10m, 11m, 9m, 10.5m, 1000);

        Assert.Throws<DomainException>(() => candle.UpdateFrom(other));
    }

    [Fact]
    public void RiskLimits_rejeita_perda_diaria_maior_que_drawdown()
    {
        Assert.Throws<DomainException>(() => new RiskLimits(20m, 10m, 20m, 5, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void RiskLimits_rejeita_percentual_invalido(decimal percent)
    {
        Assert.Throws<DomainException>(() => new RiskLimits(1m, percent, 20m, 5, 2));
    }

    [Fact]
    public void TradingState_nasce_travado_e_exige_liberacao()
    {
        var state = TradingState.CreateHalted(Now);
        Assert.True(state.IsHalted);

        state.Resume("caio", Now);
        Assert.False(state.IsHalted);
        Assert.Throws<DomainException>(() => state.Resume("caio", Now));

        state.Halt("teste", "caio", Now);
        Assert.True(state.IsHalted);
        Assert.Equal("teste", state.HaltReason);
    }
}
