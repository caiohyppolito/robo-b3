using RoboB3.Domain.Common;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Learning;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Paper;

/// <summary>Posição aberta na conta simulada.</summary>
public sealed class PaperPosition
{
    private PaperPosition()
    {
        Symbol = string.Empty;
        ContractSymbol = string.Empty;
        StrategyKey = string.Empty;
        Features = [];
    }

    internal PaperPosition(
        Guid? assignmentId,
        string symbol,
        string contractSymbol,
        string strategyKey,
        PositionDirection direction,
        int quantity,
        decimal entryPrice,
        decimal? stop,
        decimal multiplier,
        bool isFuture,
        decimal entryCost,
        FeatureVector? features,
        double? probability,
        DateTimeOffset entryTime,
        ExecutionMode mode = ExecutionMode.Paper,
        long? brokerTicket = null)
    {
        if (direction == PositionDirection.Flat)
        {
            throw new DomainException("Posição precisa ser comprada ou vendida.");
        }

        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        ContractSymbol = Guard.NotBlank(contractSymbol, nameof(contractSymbol)).ToUpperInvariant();
        StrategyKey = strategyKey;
        Direction = direction;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        EntryPrice = Guard.Positive(entryPrice, nameof(entryPrice));
        Stop = stop;
        Multiplier = Guard.Positive(multiplier, nameof(multiplier));
        IsFuture = isFuture;
        EntryCost = entryCost;
        Features = features?.Values.ToList() ?? [];
        Probability = probability;
        EntryTime = entryTime;
        MarkPrice = entryPrice;
        MarkTime = entryTime;
        Mode = mode;
        BrokerTicket = brokerTicket;
    }

    public Guid Id { get; private set; }

    public Guid? AssignmentId { get; private set; }

    public ExecutionMode Mode { get; private set; } = ExecutionMode.Paper;

    /// <summary>Ticket da posição no MetaTrader 5 (modos com corretora).</summary>
    public long? BrokerTicket { get; private set; }

    /// <summary>Ativo no cadastro (ex.: WIN).</summary>
    public string Symbol { get; private set; }

    /// <summary>Símbolo negociado na corretora (ex.: WINZ26).</summary>
    public string ContractSymbol { get; private set; }

    public string StrategyKey { get; private set; }

    public PositionDirection Direction { get; private set; }

    public int Quantity { get; private set; }

    public decimal EntryPrice { get; private set; }

    public decimal? Stop { get; private set; }

    public decimal Multiplier { get; private set; }

    public bool IsFuture { get; private set; }

    public decimal EntryCost { get; private set; }

    public IReadOnlyList<float> Features { get; private set; }

    public double? Probability { get; private set; }

    public DateTimeOffset EntryTime { get; private set; }

    public int BarsHeld { get; private set; }

    /// <summary>Último preço usado para marcar a mercado.</summary>
    public decimal MarkPrice { get; private set; }

    public DateTimeOffset MarkTime { get; private set; }

    public decimal UnrealizedProfit => (MarkPrice - EntryPrice) * Quantity * Multiplier * (int)Direction;

    public decimal Notional => MarkPrice * Quantity * Multiplier;

    public void UpdateMark(decimal price, DateTimeOffset time)
    {
        MarkPrice = Guard.Positive(price, nameof(price));
        MarkTime = time;
    }

    public void CountBar() => BarsHeld++;

    /// <summary>Stop móvel: só aperta, nunca afrouxa.</summary>
    public bool TightenStop(decimal candidate)
    {
        var tighter = Stop is null
            || (Direction == PositionDirection.Long && candidate > Stop)
            || (Direction == PositionDirection.Short && candidate < Stop);
        if (tighter)
        {
            Stop = candidate;
        }

        return tighter;
    }

    public bool IsStopHit(decimal bid, decimal ask) =>
        Stop is { } stop && (Direction == PositionDirection.Long ? bid > 0 && bid <= stop : ask > 0 && ask >= stop);

    public FeatureVector? FeatureVector => Features.Count == Learning.FeatureVector.Count ? new FeatureVector(Features) : null;
}
