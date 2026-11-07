namespace RoboB3.Application.Execution;

/// <summary>
/// Travas de configuração para enviar ordens (seção "Execution"). Ambas nascem desligadas.
/// </summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    /// <summary>Permite os modos que enviam ordens ao MetaTrader 5.</summary>
    public bool AllowMt5 { get; set; }

    /// <summary>Permite operar a conta real (além de AllowMt5).</summary>
    public bool AllowRealAccount { get; set; }
}
