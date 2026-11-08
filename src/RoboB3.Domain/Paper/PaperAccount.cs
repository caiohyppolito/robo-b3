using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Learning;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Paper;

/// <summary>
/// Livro do robô em um modo de execução (simulado, MT5 demo ou MT5 real): caixa realizado, pico e base do dia.
/// O patrimônio total soma o resultado aberto das posições. Cada modo tem o seu livro.
/// </summary>
public sealed class PaperAccount
{
    public const int SingletonId = (int)ExecutionMode.Paper;

    private PaperAccount()
    {
    }

    private PaperAccount(decimal initialCapital, DateTimeOffset now, ExecutionMode mode)
    {
        Id = (int)mode;
        InitialCapital = Guard.Positive(initialCapital, nameof(initialCapital));
        Cash = initialCapital;
        PeakEquity = initialCapital;
        DayStartEquity = initialCapital;
        CurrentDay = MarketData.B3Calendar.TradingDate(now);
        CreatedAt = now;
    }

    public int Id { get; private set; }

    public ExecutionMode Mode => (ExecutionMode)Id;

    public decimal InitialCapital { get; private set; }

    public decimal Cash { get; private set; }

    public decimal PeakEquity { get; private set; }

    public decimal DayStartEquity { get; private set; }

    public DateOnly CurrentDay { get; private set; }

    public decimal CostsPaid { get; private set; }

    public decimal RealizedProfit { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PaperAccount Open(decimal initialCapital, DateTimeOffset now, ExecutionMode mode = ExecutionMode.Paper) =>
        new(initialCapital, now, mode);

    public decimal Equity(IEnumerable<PaperPosition> positions) =>
        Cash + (positions ?? throw new ArgumentNullException(nameof(positions))).Sum(p => p.UnrealizedProfit);

    /// <summary>Atualiza o pico e, na virada do pregão, a base da perda diária.</summary>
    public void Refresh(decimal equity, DateOnly today)
    {
        if (today > CurrentDay)
        {
            CurrentDay = today;
            DayStartEquity = equity;
        }

        PeakEquity = Math.Max(PeakEquity, equity);
    }

    public PortfolioSnapshot Snapshot(IReadOnlyCollection<PaperPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var exposures = positions
            .Select(p => new PositionExposure(p.Symbol, p.IsFuture ? MarketData.AssetClass.Future : MarketData.AssetClass.Stock,
                p.Quantity * (int)p.Direction, p.Notional))
            .ToList();
        return new PortfolioSnapshot(Math.Max(0, Equity(positions)), DayStartEquity, PeakEquity, exposures);
    }

    public (PaperPosition Position, PaperFill Fill) OpenPosition(
        Guid? assignmentId,
        InstrumentSpec instrument,
        string contractSymbol,
        string strategyKey,
        PositionDirection direction,
        int quantity,
        decimal executedPrice,
        decimal? stop,
        CostModel costs,
        FeatureVector? features,
        double? probability,
        DateTimeOffset time,
        long? brokerTicket = null,
        decimal? target = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(costs);
        var isBuy = direction == PositionDirection.Long;
        var price = executedPrice;
        var cost = costs.OrderCost(price * quantity * instrument.Multiplier, quantity, instrument.IsFuture);

        var position = new PaperPosition(assignmentId, instrument.Symbol, contractSymbol, strategyKey, direction, quantity, price, stop,
            instrument.Multiplier, instrument.IsFuture, cost, features, probability, time, Mode, brokerTicket, target);
        Cash -= cost;
        CostsPaid += cost;

        var fill = new PaperFill(position.Id, contractSymbol, isBuy ? OrderSide.Buy : OrderSide.Sell, quantity, Math.Round(price, 4), Math.Round(cost, 2), null, "entrada", time, Mode, strategyKey);
        return (position, fill);
    }

    /// <param name="executedPrice">Preço efetivo da saída (já com slippage na simulação, ou o preço da corretora).</param>
    public (PaperFill Fill, TradeJournalEntry Journal) ClosePosition(PaperPosition position, decimal executedPrice, CostModel costs, string reason, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(costs);
        var isBuy = position.Direction == PositionDirection.Short;
        var price = executedPrice;
        var gross = (price - position.EntryPrice) * position.Quantity * position.Multiplier * (int)position.Direction;
        var cost = costs.OrderCost(price * position.Quantity * position.Multiplier, position.Quantity, position.IsFuture);
        var net = gross - cost - position.EntryCost;

        Cash += gross - cost;
        CostsPaid += cost;
        RealizedProfit += net;
        position.UpdateMark(price, time);

        var invested = position.EntryPrice * position.Quantity * position.Multiplier;
        var journal = new TradeJournalEntry(
            Mode == ExecutionMode.Mt5Real ? TradeSource.Live : TradeSource.Paper,
            position.StrategyKey,
            position.Symbol,
            position.Direction,
            MarketData.B3Calendar.TradingDate(position.EntryTime),
            MarketData.B3Calendar.TradingDate(time),
            Math.Round(net, 2),
            invested == 0 ? 0 : Math.Round(net / invested * 100m, 2),
            position.FeatureVector,
            position.Probability,
            time,
            Math.Round(position.MaxAdverse / position.EntryPrice * 100m, 3),
            Math.Round(position.MaxFavorable / position.EntryPrice * 100m, 3));

        var fill = new PaperFill(position.Id, position.ContractSymbol, isBuy ? OrderSide.Buy : OrderSide.Sell, position.Quantity,
            Math.Round(price, 4), Math.Round(cost, 2), Math.Round(net, 2), reason, time, Mode, position.StrategyKey);
        return (fill, journal);
    }
}
