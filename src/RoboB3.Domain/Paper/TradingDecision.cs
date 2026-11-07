using RoboB3.Domain.Common;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Paper;

public enum DecisionOutcome
{
    /// <summary>Sem sinal de entrada.</summary>
    NoSignal = 1,

    /// <summary>Manteve a posição aberta.</summary>
    Held = 2,

    Entered = 3,

    Exited = 4,

    /// <summary>Entrada impedida por trava (robô travado, horário, limite, contrato ocupado, sem cotação).</summary>
    Blocked = 5,

    /// <summary>Entrada vetada pela IA.</summary>
    Vetoed = 6,

    /// <summary>Entrada recusada pelo gestor de risco.</summary>
    Rejected = 7,

    /// <summary>A corretora não executou (ou não confirmou) a ordem.</summary>
    Failed = 8,
}

/// <summary>O que o robô decidiu numa barra fechada de uma alocação, e por quê. Fica para conferência.</summary>
public sealed class TradingDecision
{
    private TradingDecision()
    {
        Symbol = string.Empty;
        Contract = string.Empty;
        StrategyKey = string.Empty;
        Detail = string.Empty;
    }

    public TradingDecision(
        DateTimeOffset time,
        ExecutionMode mode,
        Guid assignmentId,
        string symbol,
        string contract,
        string strategyKey,
        DateTimeOffset barTime,
        PositionDirection signal)
    {
        Time = time;
        Mode = mode;
        AssignmentId = assignmentId;
        Symbol = Guard.NotBlank(symbol, nameof(symbol));
        Contract = Guard.NotBlank(contract, nameof(contract));
        StrategyKey = Guard.NotBlank(strategyKey, nameof(strategyKey));
        BarTime = barTime;
        Signal = signal;
        Outcome = DecisionOutcome.NoSignal;
        Detail = "Sem sinal de entrada.";
    }

    public long Id { get; private set; }

    public DateTimeOffset Time { get; private set; }

    public ExecutionMode Mode { get; private set; }

    public Guid AssignmentId { get; private set; }

    public string Symbol { get; private set; }

    public string Contract { get; private set; }

    public string StrategyKey { get; private set; }

    public DateTimeOffset BarTime { get; private set; }

    public PositionDirection Signal { get; private set; }

    public DecisionOutcome Outcome { get; private set; }

    public string Detail { get; private set; }

    public double? Probability { get; private set; }

    public int? Quantity { get; private set; }

    public decimal? Price { get; private set; }

    public decimal? Stop { get; private set; }

    /// <summary>Conta do tamanho da posição (nas entradas).</summary>
    public string? Sizing { get; private set; }

    public void Conclude(DecisionOutcome outcome, string detail)
    {
        Outcome = outcome;
        Detail = Guard.NotBlank(detail, nameof(detail));
    }

    public void SetProbability(double probability) => Probability = probability;

    public void SetSizing(string sizing) => Sizing = sizing;

    public void SetExecution(int quantity, decimal price, decimal? stop)
    {
        Quantity = quantity;
        Price = price;
        Stop = stop;
    }
}
