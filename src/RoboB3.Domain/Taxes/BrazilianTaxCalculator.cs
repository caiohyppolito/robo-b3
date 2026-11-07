namespace RoboB3.Domain.Taxes;

public enum TaxCategory
{
    /// <summary>Ações em operação comum (swing): 15%, isenção se vendas de ações no mês ≤ R$ 20 mil.</summary>
    StockSwing = 1,

    /// <summary>ETFs e futuros em operação comum: 15%, sem isenção.</summary>
    OtherSwing = 2,

    /// <summary>Day trade (compra e venda no mesmo dia): 20%.</summary>
    DayTrade = 3,
}

/// <summary>Operação encerrada, com resultado líquido de custos.</summary>
public sealed record RealizedTrade(DateOnly ExitDate, TaxCategory Category, decimal NetProfit, decimal SaleAmount);

/// <summary>
/// Apuração mensal simplificada do IR em renda variável:
/// 15% em operações comuns, 20% em day trade, isenção de ações até R$ 20 mil de vendas no mês,
/// compensação de prejuízos separada por modalidade e DARF mínimo de R$ 10 acumulado para o mês seguinte.
/// Não substitui um contador; o IRRF ("dedo-duro") é só antecipação e não altera o total.
/// </summary>
public sealed class BrazilianTaxCalculator
{
    public const decimal StockExemptionLimit = 20_000m;
    public const decimal SwingRate = 0.15m;
    public const decimal DayTradeRate = 0.20m;
    public const decimal MinimumDarf = 10m;

    public decimal SwingLossCarryForward { get; private set; }

    public decimal DayTradeLossCarryForward { get; private set; }

    /// <summary>Imposto apurado abaixo do DARF mínimo, que fica para o mês seguinte.</summary>
    public decimal PendingTax { get; private set; }

    /// <summary>Fecha o mês e retorna o imposto a pagar (DARF).</summary>
    public decimal CloseMonth(IEnumerable<RealizedTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var list = trades.ToList();

        var stockSales = list.Where(t => t.Category == TaxCategory.StockSwing).Sum(t => t.SaleAmount);
        var stockResult = list.Where(t => t.Category == TaxCategory.StockSwing).Sum(t => t.NetProfit);
        var otherResult = list.Where(t => t.Category == TaxCategory.OtherSwing).Sum(t => t.NetProfit);
        var dayTradeResult = list.Where(t => t.Category == TaxCategory.DayTrade).Sum(t => t.NetProfit);

        // Lucro isento não é tributado nem consome prejuízo acumulado; prejuízo continua compensável.
        var exempt = stockSales <= StockExemptionLimit && stockResult > 0;
        var swingResult = (exempt ? 0m : stockResult) + otherResult;

        var swingTaxable = Offset(swingResult, SwingLossCarryForward, out var swingCarry);
        SwingLossCarryForward = swingCarry;
        var dayTradeTaxable = Offset(dayTradeResult, DayTradeLossCarryForward, out var dayTradeCarry);
        DayTradeLossCarryForward = dayTradeCarry;

        var tax = (swingTaxable * SwingRate) + (dayTradeTaxable * DayTradeRate) + PendingTax;
        if (tax < MinimumDarf)
        {
            PendingTax = tax;
            return 0m;
        }

        PendingTax = 0m;
        return Math.Round(tax, 2);
    }

    /// <summary>Imposto pendente (abaixo do mínimo) que ainda será pago ao fim do período.</summary>
    public decimal Flush()
    {
        var tax = PendingTax;
        PendingTax = 0m;
        return Math.Round(tax, 2);
    }

    private static decimal Offset(decimal result, decimal carry, out decimal newCarry)
    {
        if (result <= 0)
        {
            newCarry = carry - result;
            return 0m;
        }

        var used = Math.Min(carry, result);
        newCarry = carry - used;
        return result - used;
    }
}
