using RoboB3.Domain.Taxes;

namespace RoboB3.Domain.Tests;

public class TaxCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void Lucro_em_acoes_com_vendas_ate_20_mil_e_isento()
    {
        var calc = new BrazilianTaxCalculator();

        var tax = calc.CloseMonth([new RealizedTrade(Day, TaxCategory.StockSwing, 3_000m, 19_000m)]);

        Assert.Equal(0m, tax);
    }

    [Fact]
    public void Lucro_em_acoes_com_vendas_acima_de_20_mil_paga_15_porcento()
    {
        var calc = new BrazilianTaxCalculator();

        var tax = calc.CloseMonth([new RealizedTrade(Day, TaxCategory.StockSwing, 3_000m, 25_000m)]);

        Assert.Equal(450m, tax);
    }

    [Fact]
    public void Etf_nao_tem_isencao()
    {
        var calc = new BrazilianTaxCalculator();

        var tax = calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, 1_000m, 5_000m)]);

        Assert.Equal(150m, tax);
    }

    [Fact]
    public void Prejuizo_e_compensado_nos_meses_seguintes()
    {
        var calc = new BrazilianTaxCalculator();

        Assert.Equal(0m, calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, -2_000m, 10_000m)]));
        Assert.Equal(2_000m, calc.SwingLossCarryForward);

        var tax = calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, 3_000m, 10_000m)]);

        Assert.Equal(150m, tax);
        Assert.Equal(0m, calc.SwingLossCarryForward);
    }

    [Fact]
    public void Lucro_isento_nao_consome_prejuizo_acumulado()
    {
        var calc = new BrazilianTaxCalculator();
        calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, -1_000m, 10_000m)]);

        calc.CloseMonth([new RealizedTrade(Day, TaxCategory.StockSwing, 5_000m, 15_000m)]);

        Assert.Equal(1_000m, calc.SwingLossCarryForward);
    }

    [Fact]
    public void Day_trade_paga_20_porcento_e_nao_compensa_com_swing()
    {
        var calc = new BrazilianTaxCalculator();

        var tax = calc.CloseMonth(
        [
            new RealizedTrade(Day, TaxCategory.DayTrade, 1_000m, 50_000m),
            new RealizedTrade(Day, TaxCategory.OtherSwing, -1_000m, 50_000m),
        ]);

        Assert.Equal(200m, tax);
        Assert.Equal(1_000m, calc.SwingLossCarryForward);
    }

    [Fact]
    public void Imposto_abaixo_de_10_reais_acumula_para_o_mes_seguinte()
    {
        var calc = new BrazilianTaxCalculator();

        Assert.Equal(0m, calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, 40m, 1_000m)]));
        Assert.Equal(6m, calc.PendingTax);

        Assert.Equal(12m, calc.CloseMonth([new RealizedTrade(Day, TaxCategory.OtherSwing, 40m, 1_000m)]));
        Assert.Equal(0m, calc.PendingTax);
    }
}
