using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Tests;

public class RiskManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);
    private static readonly RiskLimits Limits = new(2m, 15m, 20m, 3, 2);

    private static TradingState Running()
    {
        var state = TradingState.CreateHalted(Now);
        state.Resume("teste", Now);
        return state;
    }

    private static PortfolioSnapshot Portfolio(decimal equity = 100_000m, params PositionExposure[] positions) =>
        new(equity, 100_000m, 100_000m, positions);

    [Fact]
    public void Aprova_ordem_dentro_dos_limites()
    {
        var order = new OrderRequest("PETR4", AssetClass.Stock, OrderSide.Buy, 500, 38m);

        var decision = RiskManager.Evaluate(order, Portfolio(), Limits, Running());

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void Rejeita_quando_robo_esta_travado()
    {
        var order = new OrderRequest("PETR4", AssetClass.Stock, OrderSide.Buy, 100, 38m);

        var decision = RiskManager.Evaluate(order, Portfolio(), Limits, TradingState.CreateHalted(Now));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Violations, v => v.StartsWith("Robô travado", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_exposicao_acima_do_limite_por_ativo()
    {
        // 20% de 100 mil = 20 mil; 600 x 38 = 22.800.
        var order = new OrderRequest("PETR4", AssetClass.Stock, OrderSide.Buy, 600, 38m);

        var decision = RiskManager.Evaluate(order, Portfolio(), Limits, Running());

        Assert.False(decision.IsApproved);
    }

    [Fact]
    public void Considera_posicao_existente_no_calculo_de_exposicao()
    {
        var existing = new PositionExposure("PETR4", AssetClass.Stock, 400, 15_200m);
        var order = new OrderRequest("PETR4", AssetClass.Stock, OrderSide.Buy, 200, 38m);

        var decision = RiskManager.Evaluate(order, Portfolio(100_000m, existing), Limits, Running());

        Assert.False(decision.IsApproved);
    }

    [Fact]
    public void Rejeita_nova_posicao_quando_limite_de_posicoes_atingido()
    {
        var positions = new[]
        {
            new PositionExposure("VALE3", AssetClass.Stock, 100, 6_000m),
            new PositionExposure("ITUB4", AssetClass.Stock, 100, 3_500m),
            new PositionExposure("BBAS3", AssetClass.Stock, 100, 2_800m),
        };
        var order = new OrderRequest("PETR4", AssetClass.Stock, OrderSide.Buy, 100, 38m);

        var decision = RiskManager.Evaluate(order, Portfolio(100_000m, positions), Limits, Running());

        Assert.Contains(decision.Violations, v => v.Contains("posições abertas", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_contratos_futuros_acima_do_limite()
    {
        var existing = new PositionExposure("WDO", AssetClass.Future, 1, 0m);
        var order = new OrderRequest("WIN", AssetClass.Future, OrderSide.Sell, 2, 130_000m);

        var decision = RiskManager.Evaluate(order, Portfolio(100_000m, existing), Limits, Running());

        Assert.Contains(decision.Violations, v => v.Contains("contratos futuros", StringComparison.Ordinal));
    }

    [Fact]
    public void Zerar_posicao_e_sempre_permitido_mesmo_travado()
    {
        var existing = new PositionExposure("WIN", AssetClass.Future, -2, 0m);
        var order = new OrderRequest("WIN", AssetClass.Future, OrderSide.Buy, 2, 130_000m);
        var lossDay = new PortfolioSnapshot(90_000m, 100_000m, 100_000m, [existing]);

        var decision = RiskManager.Evaluate(order, lossDay, Limits, TradingState.CreateHalted(Now));

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void Virar_a_mao_nao_e_tratado_como_reducao()
    {
        var existing = new PositionExposure("WIN", AssetClass.Future, -1, 0m);
        var order = new OrderRequest("WIN", AssetClass.Future, OrderSide.Buy, 2, 130_000m);

        var decision = RiskManager.Evaluate(order, Portfolio(100_000m, existing), Limits, TradingState.CreateHalted(Now));

        Assert.False(decision.IsApproved);
    }

    [Theory]
    [InlineData(98_000, 100_000, true)]
    [InlineData(98_500, 100_000, false)]
    public void Detecta_perda_diaria(decimal equity, decimal startOfDay, bool expectBreach)
    {
        var snapshot = new PortfolioSnapshot(equity, startOfDay, 100_000m, []);

        var breach = RiskManager.FindGlobalBreach(snapshot, Limits);

        Assert.Equal(expectBreach, breach is not null);
    }

    [Fact]
    public void Detecta_drawdown_a_partir_do_pico()
    {
        var snapshot = new PortfolioSnapshot(102_000m, 102_000m, 120_000m, []);

        var breach = RiskManager.FindGlobalBreach(snapshot, Limits);

        Assert.NotNull(breach);
        Assert.Contains("Drawdown", breach, StringComparison.Ordinal);
    }
}
