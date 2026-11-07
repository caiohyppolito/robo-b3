namespace RoboB3.Domain.Backtesting;

/// <summary>Backtest executado e guardado para consulta e comparação.</summary>
public sealed class BacktestRun
{
    private BacktestRun()
    {
        Symbol = string.Empty;
        StrategyKey = string.Empty;
        Parameters = new Dictionary<string, decimal>();
        Metrics = null!;
        Trades = [];
        Equity = [];
        Events = [];
    }

    public BacktestRun(BacktestResult result, BacktestSettings settings, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        Id = Guid.NewGuid();
        CreatedAt = createdAt;
        Symbol = result.Symbol;
        StrategyKey = result.StrategyKey;
        Parameters = new Dictionary<string, decimal>(result.Parameters.Values);
        From = result.Equity[0].Date;
        To = result.Equity[^1].Date;
        RiskPerTradePercent = settings.RiskPerTradePercent;
        ApplyTaxes = settings.ApplyTaxes;
        Metrics = result.Metrics;
        Trades = result.Trades;
        Equity = result.Equity;
        Events = result.Events;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string Symbol { get; private set; }

    public string StrategyKey { get; private set; }

    public IReadOnlyDictionary<string, decimal> Parameters { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public decimal RiskPerTradePercent { get; private set; }

    public bool ApplyTaxes { get; private set; }

    public PerformanceMetrics Metrics { get; private set; }

    public IReadOnlyList<BacktestTrade> Trades { get; private set; }

    public IReadOnlyList<EquityPoint> Equity { get; private set; }

    public IReadOnlyList<BacktestEvent> Events { get; private set; }
}
