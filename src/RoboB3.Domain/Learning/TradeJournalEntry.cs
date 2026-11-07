using RoboB3.Domain.Common;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Learning;

public enum TradeSource
{
    Paper = 1,
    Live = 2,
}

/// <summary>
/// Diário de operações executadas pelo robô (simulado ou real), com o contexto da entrada.
/// É a memória de onde a IA aprende com os próprios erros; pesa mais que dados de backtest.
/// </summary>
public sealed class TradeJournalEntry
{
    private TradeJournalEntry()
    {
        StrategyKey = string.Empty;
        Symbol = string.Empty;
        Features = [];
    }

    public TradeJournalEntry(
        TradeSource source,
        string strategyKey,
        string symbol,
        PositionDirection direction,
        DateOnly entryDate,
        DateOnly exitDate,
        decimal netProfit,
        decimal returnPercent,
        FeatureVector? features,
        double? modelProbability,
        DateTimeOffset recordedAt)
    {
        if (exitDate < entryDate)
        {
            throw new DomainException("A saída não pode ser anterior à entrada.");
        }

        Id = Guid.NewGuid();
        Source = source;
        StrategyKey = Guard.NotBlank(strategyKey, nameof(strategyKey));
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        Direction = direction;
        EntryDate = entryDate;
        ExitDate = exitDate;
        NetProfit = netProfit;
        ReturnPercent = returnPercent;
        Features = features?.Values.ToList() ?? [];
        ModelProbability = modelProbability;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public TradeSource Source { get; private set; }

    public string StrategyKey { get; private set; }

    public string Symbol { get; private set; }

    public PositionDirection Direction { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public DateOnly ExitDate { get; private set; }

    public decimal NetProfit { get; private set; }

    public decimal ReturnPercent { get; private set; }

    public IReadOnlyList<float> Features { get; private set; }

    /// <summary>Chance de acerto que a IA estimou na entrada (se havia modelo ativo).</summary>
    public double? ModelProbability { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public bool IsWin => NetProfit > 0;

    public LabeledExample? ToExample(float weight) =>
        Features.Count == FeatureVector.Count
            ? new LabeledExample(Symbol, EntryDate, ExitDate, new FeatureVector(Features), IsWin, ReturnPercent, weight)
            : null;
}
