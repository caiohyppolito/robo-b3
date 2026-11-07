namespace RoboB3.Domain.Execution;

/// <summary>Para onde vão as ordens do robô.</summary>
public enum ExecutionMode
{
    /// <summary>Execução simulada interna, com preços reais. Nada vai para a corretora.</summary>
    Paper = 1,

    /// <summary>Ordens enviadas ao MetaTrader 5 numa conta demo.</summary>
    Mt5Demo = 2,

    /// <summary>Ordens enviadas ao MetaTrader 5 na conta real. Dinheiro de verdade.</summary>
    Mt5Real = 3,
}

public static class ExecutionModeExtensions
{
    public static bool SendsToBroker(this ExecutionMode mode) => mode != ExecutionMode.Paper;

    /// <summary>Tipo de conta exigido no MT5 (nulo no modo simulado).</summary>
    public static bool? RequiresDemoAccount(this ExecutionMode mode) => mode switch
    {
        ExecutionMode.Mt5Demo => true,
        ExecutionMode.Mt5Real => false,
        _ => null,
    };

    public static string Describe(this ExecutionMode mode) => mode switch
    {
        ExecutionMode.Paper => "simulado",
        ExecutionMode.Mt5Demo => "MT5 conta demo",
        ExecutionMode.Mt5Real => "MT5 CONTA REAL",
        _ => mode.ToString(),
    };
}
