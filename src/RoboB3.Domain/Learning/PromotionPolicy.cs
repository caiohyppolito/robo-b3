namespace RoboB3.Domain.Learning;

/// <summary>Resultado econômico fora da amostra: Sharpe médio dos ativos com e sem o filtro.</summary>
public sealed record EconomicTest(decimal UnfilteredSharpe, decimal CandidateSharpe, decimal? ChampionSharpe);

public sealed record PromotionDecision(bool PromoteCandidate, bool RetireChampion, string Reason);

/// <summary>
/// Regras para um modelo novo substituir o atual. Tudo é medido fora da amostra;
/// sem evidência de melhora, o robô fica com o que já tem (ou sem filtro).
/// </summary>
public sealed record PromotionPolicy(
    int MinValidationExamples = 15,
    double MinAuc = 0.55,
    decimal MinSharpeImprovement = 0.05m,
    decimal MinKeptPercent = 20m)
{
    public PromotionDecision Decide(ValidationReport validation, EconomicTest economics)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(economics);

        // Um modelo atual que já não supera operar sem filtro é aposentado, haja o que houver com o novo.
        var championStale = economics.ChampionSharpe is { } champion && champion < economics.UnfilteredSharpe;

        string? rejection = null;
        if (validation.Examples < MinValidationExamples)
        {
            rejection = $"Poucas operações fora da amostra ({validation.Examples} < {MinValidationExamples}).";
        }
        else if (validation.Auc < MinAuc)
        {
            rejection = $"AUC {validation.Auc:F2} abaixo de {MinAuc:F2}: o modelo não separa acertos de erros melhor que o acaso.";
        }
        else if (validation.KeptPercent < MinKeptPercent)
        {
            rejection = $"Filtraria {100 - validation.KeptPercent:F0}% dos sinais: restrito demais para ser confiável.";
        }
        else if (economics.CandidateSharpe < economics.UnfilteredSharpe + MinSharpeImprovement)
        {
            rejection = $"Sharpe com filtro {economics.CandidateSharpe:F2} não supera o sem filtro {economics.UnfilteredSharpe:F2} pela margem de {MinSharpeImprovement:F2}.";
        }
        else if (economics.ChampionSharpe is { } current && economics.CandidateSharpe <= current)
        {
            rejection = $"Sharpe {economics.CandidateSharpe:F2} não supera o do modelo atual ({current:F2}).";
        }

        if (rejection is not null)
        {
            return new PromotionDecision(false, championStale,
                championStale ? $"{rejection} Modelo atual aposentado: já não supera operar sem filtro." : rejection);
        }

        return new PromotionDecision(true, economics.ChampionSharpe is not null,
            $"Promovido: Sharpe fora da amostra {economics.CandidateSharpe:F2} contra {economics.UnfilteredSharpe:F2} sem filtro"
            + (economics.ChampionSharpe is { } c ? $" e {c:F2} do modelo anterior." : "."));
    }
}
