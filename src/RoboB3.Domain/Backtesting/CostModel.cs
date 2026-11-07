using RoboB3.Domain.Common;

namespace RoboB3.Domain.Backtesting;

/// <summary>
/// Custos por ordem. Os padrões aproximam a Clear (corretagem zero) e as taxas da B3;
/// confira a tabela vigente e ajuste se necessário.
/// </summary>
public sealed record CostModel
{
    public CostModel(decimal brokeragePerOrder, decimal feesPercent, decimal slippagePercent, decimal futuresFeePerContract)
    {
        BrokeragePerOrder = Guard.NotNegative(brokeragePerOrder, nameof(brokeragePerOrder));
        FeesPercent = Guard.NotNegative(feesPercent, nameof(feesPercent));
        SlippagePercent = Guard.NotNegative(slippagePercent, nameof(slippagePercent));
        FuturesFeePerContract = Guard.NotNegative(futuresFeePerContract, nameof(futuresFeePerContract));
    }

    /// <summary>Corretagem fixa por ordem (Clear: R$ 0).</summary>
    public decimal BrokeragePerOrder { get; }

    /// <summary>Emolumentos + liquidação da B3 sobre o volume, em % (ações/ETFs).</summary>
    public decimal FeesPercent { get; }

    /// <summary>Diferença média entre o preço esperado e o executado, em %.</summary>
    public decimal SlippagePercent { get; }

    /// <summary>Taxas da B3 por contrato futuro (por lado).</summary>
    public decimal FuturesFeePerContract { get; }

    public static CostModel Default { get; } = new(0m, 0.03m, 0.05m, 0.50m);

    public decimal OrderCost(decimal notional, int quantity, bool isFuture) =>
        BrokeragePerOrder + (isFuture ? quantity * FuturesFeePerContract : notional * FeesPercent / 100m);

    /// <summary>Preço de execução piorado pelo slippage (compra mais caro, vende mais barato).</summary>
    public decimal Fill(decimal price, bool isBuy) =>
        price * (1m + ((isBuy ? 1 : -1) * SlippagePercent / 100m));
}
