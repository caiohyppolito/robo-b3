using System.ComponentModel.DataAnnotations;
using RoboB3.Domain.Backtesting;

namespace RoboB3.Application.Backtesting;

/// <summary>Padrões de backtest (seção "Backtest" da configuração).</summary>
public sealed class BacktestOptions
{
    public const string SectionName = "Backtest";

    /// <summary>CDI anual aproximado. Ajuste para a taxa vigente.</summary>
    [Range(0, 100)]
    public decimal RiskFreeRatePercent { get; set; } = 10m;

    [Range(0, 1000)]
    public decimal BrokeragePerOrder { get; set; } = CostModel.Default.BrokeragePerOrder;

    [Range(0, 5)]
    public decimal FeesPercent { get; set; } = CostModel.Default.FeesPercent;

    [Range(0, 5)]
    public decimal SlippagePercent { get; set; } = CostModel.Default.SlippagePercent;

    [Range(0, 100)]
    public decimal FuturesFeePerContract { get; set; } = CostModel.Default.FuturesFeePerContract;

    public CostModel Costs() => new(BrokeragePerOrder, FeesPercent, SlippagePercent, FuturesFeePerContract);
}
