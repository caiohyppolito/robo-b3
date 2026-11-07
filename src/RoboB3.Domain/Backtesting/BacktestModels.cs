using RoboB3.Domain.Common;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Backtesting;

public sealed record BacktestSettings
{
    public BacktestSettings(
        decimal initialCapital,
        decimal riskPerTradePercent,
        RiskLimits limits,
        CostModel costs,
        decimal riskFreeRatePercent,
        bool applyTaxes,
        bool earnRiskFreeOnCash = true,
        bool flattenAtDayEnd = false)
    {
        InitialCapital = Guard.Positive(initialCapital, nameof(initialCapital));
        RiskPerTradePercent = Guard.Percentage(riskPerTradePercent, nameof(riskPerTradePercent));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
        RiskFreeRatePercent = Guard.NotNegative(riskFreeRatePercent, nameof(riskFreeRatePercent));
        ApplyTaxes = applyTaxes;
        EarnRiskFreeOnCash = earnRiskFreeOnCash;
        FlattenAtDayEnd = flattenAtDayEnd;
    }

    public decimal InitialCapital { get; }

    /// <summary>Quanto do patrimônio se aceita perder se o stop for atingido.</summary>
    public decimal RiskPerTradePercent { get; }

    public RiskLimits Limits { get; }

    public CostModel Costs { get; }

    /// <summary>Taxa livre de risco anual (CDI) para Sharpe e comparação.</summary>
    public decimal RiskFreeRatePercent { get; }

    public bool ApplyTaxes { get; }

    /// <summary>
    /// Caixa parado rende CDI (como numa conta remunerada ou no Tesouro Selic).
    /// O IR da renda fixa não é descontado, nem aqui nem na linha do CDI.
    /// </summary>
    public bool EarnRiskFreeOnCash { get; }

    /// <summary>Day trade: zera a posição na última barra de cada pregão e não leva risco para o dia seguinte.</summary>
    public bool FlattenAtDayEnd { get; }
}

public sealed record BacktestTrade(
    PositionDirection Direction,
    DateOnly EntryDate,
    decimal EntryPrice,
    DateOnly ExitDate,
    decimal ExitPrice,
    int Quantity,
    decimal GrossProfit,
    decimal Costs,
    decimal NetProfit,
    decimal ReturnPercent,
    string ExitReason,
    IReadOnlyList<float>? EntryFeatures = null,
    double? EntryProbability = null,
    DateTimeOffset? EntryTime = null,
    DateTimeOffset? ExitTime = null);

/// <summary>Patrimônio da estratégia, do "comprar e segurar" com 100% do capital e do CDI no mesmo dia.</summary>
public sealed record EquityPoint(DateOnly Date, decimal Equity, decimal Benchmark, decimal RiskFree);

public sealed record BacktestEvent(DateOnly Date, string Message);

public sealed record BacktestResult(
    string Symbol,
    string StrategyKey,
    StrategyParameters Parameters,
    IReadOnlyList<BacktestTrade> Trades,
    IReadOnlyList<EquityPoint> Equity,
    IReadOnlyList<BacktestEvent> Events,
    PerformanceMetrics Metrics);
