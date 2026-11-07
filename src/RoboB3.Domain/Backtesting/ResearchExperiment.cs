using RoboB3.Domain.Common;

namespace RoboB3.Domain.Backtesting;

/// <summary>
/// Registro de uma pesquisa (comparação de estratégias, treino de IA): quantas combinações foram testadas
/// e o resumo do resultado. Quanto mais tentativas, maior a chance de o "melhor" ser sorte: o número fica à vista.
/// </summary>
public sealed class ResearchExperiment
{
    private ResearchExperiment()
    {
        Kind = string.Empty;
        Description = string.Empty;
        Summary = string.Empty;
    }

    public ResearchExperiment(string kind, string description, int trials, string summary, string codeVersion, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Kind = Guard.NotBlank(kind, nameof(kind));
        Description = Guard.NotBlank(description, nameof(description));
        Trials = Guard.Positive(trials, nameof(trials));
        Summary = summary ?? string.Empty;
        CodeVersion = codeVersion ?? string.Empty;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Kind { get; private set; }

    public string Description { get; private set; }

    /// <summary>Quantas simulações (combinações de parâmetros × ativos × períodos) foram rodadas.</summary>
    public int Trials { get; private set; }

    /// <summary>Resumo em JSON do resultado.</summary>
    public string Summary { get; private set; }

    public string CodeVersion { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
