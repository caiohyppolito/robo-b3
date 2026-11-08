using RoboB3.Domain.Taxes;

namespace RoboB3.Domain.Tests;

public class MonthlyTaxReportTests
{
    private static TaxableTrade Trade(int year, int month, int day, TaxCategory category, decimal profit, decimal sale = 0) =>
        new(new DateOnly(year, month, day), category, profit, sale);

    [Fact]
    public void Acoes_com_vendas_ate_20_mil_sao_isentas()
    {
        var month = Assert.Single(MonthlyTaxReport.Build([Trade(2026, 3, 10, TaxCategory.StockSwing, 1_000m, 15_000m)]));

        Assert.True(month.StockExempt);
        Assert.Equal(1_000m, month.ExemptProfit);
        Assert.Equal(0m, month.DarfAmount);
    }

    [Fact]
    public void Acima_de_20_mil_paga_15_por_cento_menos_o_irrf_com_vencimento_no_ultimo_dia_util()
    {
        var month = Assert.Single(MonthlyTaxReport.Build([Trade(2026, 3, 10, TaxCategory.StockSwing, 2_000m, 30_000m)]));

        Assert.False(month.StockExempt);
        Assert.Equal(300m, month.SwingTax);
        Assert.Equal(1.5m, month.IrrfWithheld);
        Assert.Equal(298.5m, month.DarfAmount);
        Assert.Equal(new DateOnly(2026, 4, 30), month.DarfDueDate);
    }

    [Fact]
    public void Units_etfs_e_futuros_nao_tem_isencao()
    {
        var month = Assert.Single(MonthlyTaxReport.Build([Trade(2026, 3, 10, TaxCategory.OtherSwing, 1_000m, 5_000m)]));

        Assert.Equal(150m, month.SwingTax);
    }

    [Fact]
    public void Day_trade_paga_20_por_cento_e_compensa_so_com_day_trade()
    {
        var months = MonthlyTaxReport.Build([
            Trade(2026, 1, 15, TaxCategory.DayTrade, -500m),
            Trade(2026, 1, 20, TaxCategory.StockSwing, 1_000m, 25_000m),
            Trade(2026, 2, 12, TaxCategory.DayTrade, 800m),
        ]);

        Assert.Equal(500m, months[0].DayTradeLossCarry);
        Assert.Equal(150m, months[0].SwingTax);
        Assert.Equal(500m, months[1].DayTradeLossUsed);
        Assert.Equal(60m, months[1].DayTradeTax);
        Assert.Equal(8m, months[1].IrrfWithheld);
        Assert.Equal(52m, months[1].DarfAmount);
    }

    [Fact]
    public void Darf_abaixo_de_10_reais_acumula_para_o_mes_seguinte()
    {
        var months = MonthlyTaxReport.Build([
            Trade(2026, 5, 4, TaxCategory.OtherSwing, 40m, 1_000m),
            Trade(2026, 6, 3, TaxCategory.OtherSwing, 40m, 1_000m),
        ]);

        Assert.Equal(0m, months[0].DarfAmount);
        Assert.Equal(6m, months[1].CarriedFromPrevious);
        Assert.Equal(12m, months[1].DarfAmount);
    }

    [Fact]
    public void Irrf_que_sobra_passa_aos_meses_seguintes_mas_nao_ao_ano_seguinte()
    {
        var months = MonthlyTaxReport.Build([
            Trade(2025, 12, 2, TaxCategory.DayTrade, 1_000m),
            Trade(2025, 12, 3, TaxCategory.DayTrade, -990m),
            Trade(2026, 1, 5, TaxCategory.DayTrade, 300m),
        ]);

        // Dezembro: lucro líquido de 10 (imposto 2) e IRRF de 10 no dia do lucro: sobram 8.
        Assert.Equal(2m, months[0].IrrfDeducted);
        Assert.Equal(8m, months[0].IrrfCarry);

        // Janeiro: os 8 de dezembro vão para a declaração anual; só o IRRF do próprio mês (3) é deduzido.
        Assert.Equal(3m, months[1].IrrfDeducted);
        Assert.Equal(57m, months[1].TaxDue);
    }

    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2027, 3, 28)]
    public void Pascoa_e_calculada_corretamente(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BrazilianHolidays.Easter(year));
    }

    [Fact]
    public void Vencimento_pula_fim_de_semana_e_feriado()
    {
        // Janeiro de 2026 vence no último dia útil de fevereiro: sexta, 27/02.
        Assert.Equal(new DateOnly(2026, 2, 27), MonthlyTaxReport.DarfDueDate(2026, 1));
        Assert.False(BrazilianHolidays.IsBusinessDay(new DateOnly(2026, 2, 17)));
        Assert.False(BrazilianHolidays.IsBusinessDay(new DateOnly(2026, 11, 20)));
    }
}
