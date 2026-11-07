using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Paper;

/// <summary>Uma estratégia colocada para operar um ativo em um tempo gráfico.</summary>
public sealed class StrategyAssignment
{
    private StrategyAssignment()
    {
        Symbol = string.Empty;
        StrategyKey = string.Empty;
        Parameters = new Dictionary<string, decimal>();
    }

    public StrategyAssignment(
        string symbol,
        string strategyKey,
        IReadOnlyDictionary<string, decimal> parameters,
        Timeframe timeframe,
        bool useMetaModel,
        bool flattenAtDayEnd,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        StrategyKey = string.Empty;
        Parameters = new Dictionary<string, decimal>();
        CreatedAt = createdAt;
        Update(strategyKey, parameters, timeframe, useMetaModel, flattenAtDayEnd);
        IsEnabled = false;
    }

    public Guid Id { get; private set; }

    public string Symbol { get; private set; }

    public string StrategyKey { get; private set; }

    public IReadOnlyDictionary<string, decimal> Parameters { get; private set; }

    public Timeframe Timeframe { get; private set; }

    public bool UseMetaModel { get; private set; }

    /// <summary>Day trade: zera no horário de encerramento e não leva posição para o dia seguinte.</summary>
    public bool FlattenAtDayEnd { get; private set; }

    /// <summary>Nasce desligada: começar a operar é sempre uma ação explícita.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Última barra fechada já avaliada, para cada barra gerar no máximo um sinal.</summary>
    public DateTimeOffset? LastProcessedBar { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string strategyKey, IReadOnlyDictionary<string, decimal> parameters, Timeframe timeframe, bool useMetaModel, bool flattenAtDayEnd)
    {
        var definition = StrategyCatalog.Get(strategyKey);
        Parameters = new Dictionary<string, decimal>(definition.Normalize(parameters).Values);
        StrategyKey = definition.Key;
        Timeframe = timeframe;
        UseMetaModel = useMetaModel;
        FlattenAtDayEnd = flattenAtDayEnd;
        LastProcessedBar = null;
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    /// <returns>Falso se a barra já foi processada.</returns>
    public bool MarkProcessed(DateTimeOffset barTime)
    {
        if (LastProcessedBar is { } last && barTime <= last)
        {
            return false;
        }

        LastProcessedBar = barTime;
        return true;
    }

    public IStrategy CreateStrategy() =>
        StrategyCatalog.Get(StrategyKey).Create(new StrategyParameters(Parameters));
}
