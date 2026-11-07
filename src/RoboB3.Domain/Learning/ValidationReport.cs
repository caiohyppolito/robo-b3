namespace RoboB3.Domain.Learning;

/// <summary>Qualidade do modelo em operações que ele nunca viu no treino.</summary>
public sealed record ValidationReport(
    int Examples,
    double Auc,
    decimal BaseWinRatePercent,
    decimal FilteredWinRatePercent,
    decimal BaseAverageReturnPercent,
    decimal FilteredAverageReturnPercent,
    decimal KeptPercent)
{
    public static ValidationReport Calculate(IReadOnlyList<LabeledExample> examples, IReadOnlyList<double> probabilities, double threshold)
    {
        ArgumentNullException.ThrowIfNull(examples);
        ArgumentNullException.ThrowIfNull(probabilities);
        if (examples.Count != probabilities.Count)
        {
            throw new ArgumentException("Quantidade de exemplos e probabilidades difere.");
        }

        var kept = examples.Where((_, i) => probabilities[i] >= threshold).ToList();
        return new ValidationReport(
            examples.Count,
            Math.Round(ComputeAuc(probabilities, examples.Select(e => e.IsWin).ToList()), 3),
            WinRate(examples),
            WinRate(kept),
            AverageReturn(examples),
            AverageReturn(kept),
            examples.Count == 0 ? 0 : Math.Round(100m * kept.Count / examples.Count, 1));
    }

    /// <summary>
    /// Área sob a curva ROC: chance de o modelo dar nota maior a uma operação vencedora do que a uma perdedora.
    /// 0,5 = moeda; 1,0 = perfeito. Empates contam meio.
    /// </summary>
    public static double ComputeAuc(IReadOnlyList<double> scores, IReadOnlyList<bool> labels)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(labels);
        var positives = scores.Where((_, i) => labels[i]).ToList();
        var negatives = scores.Where((_, i) => !labels[i]).ToList();
        if (positives.Count == 0 || negatives.Count == 0)
        {
            return 0.5;
        }

        double wins = 0;
        foreach (var p in positives)
        {
            foreach (var n in negatives)
            {
                wins += p > n ? 1 : p == n ? 0.5 : 0;
            }
        }

        return wins / (positives.Count * (double)negatives.Count);
    }

    private static decimal WinRate(IReadOnlyCollection<LabeledExample> e) =>
        e.Count == 0 ? 0 : Math.Round(100m * e.Count(x => x.IsWin) / e.Count, 1);

    private static decimal AverageReturn(IReadOnlyCollection<LabeledExample> e) =>
        e.Count == 0 ? 0 : Math.Round(e.Average(x => x.ReturnPercent), 2);
}
