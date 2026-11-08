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
        if (useMetaModel && timeframe != Timeframe.D1)
        {
            // O modelo é treinado com variáveis de barras diárias; aplicá-lo a barras de minutos daria notas sem sentido.
            throw new DomainException("O filtro da IA só vale para barras diárias (D1): o modelo é treinado com dados diários.");
        }

        Timeframe = timeframe;
        UseMetaModel = useMetaModel;
        FlattenAtDayEnd = flattenAtDayEnd;
        LastProcessedBar = null;
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    /// <returns>Falso se a barra já foi processada.</returns>
    /// <summary>Stop de entrada armado: entra se o preço cruzar <see cref="ArmedLevel"/> até <see cref="ArmedUntil"/>.</summary>
    public PositionDirection? ArmedDirection { get; private set; }

    public decimal? ArmedLevel { get; private set; }

    public decimal? ArmedStop { get; private set; }

    public decimal? ArmedTarget { get; private set; }

    public DateTimeOffset? ArmedUntil { get; private set; }

    public void Arm(PositionDirection direction, decimal level, decimal? stop, decimal? target, DateTimeOffset until)
    {
        if (direction == PositionDirection.Flat)
        {
            throw new DomainException("Stop de entrada precisa de direção.");
        }

        ArmedDirection = direction;
        ArmedLevel = Guard.Positive(level, nameof(level));
        ArmedStop = stop;
        ArmedTarget = target;
        ArmedUntil = until;
    }

    public void Disarm()
    {
        ArmedDirection = null;
        ArmedLevel = null;
        ArmedStop = null;
        ArmedTarget = null;
        ArmedUntil = null;
    }

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
