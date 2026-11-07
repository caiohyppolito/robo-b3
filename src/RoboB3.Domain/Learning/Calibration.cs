namespace RoboB3.Domain.Learning;

/// <summary>Faixa de probabilidade prevista e quanto de fato acertou nela.</summary>
public sealed record CalibrationBin(double From, double To, int Count, double MeanPredicted, double ObservedWinRate);

/// <summary>
/// Calibração fora da amostra: se o modelo diz 70%, acerta perto de 70%?
/// Brier = erro quadrático médio da probabilidade (menor é melhor). Só vale a pena se for menor que o Brier
/// de chutar sempre a taxa de acerto da base (<see cref="BaseRateBrier"/>).
/// ECE = diferença média, ponderada pela quantidade, entre o previsto e o observado em cada faixa.
/// </summary>
public sealed record CalibrationReport(int Examples, double Brier, double BaseRateBrier, double ExpectedCalibrationError, IReadOnlyList<CalibrationBin> Bins)
{
    public static CalibrationReport Empty { get; } = new(0, 0, 0, 0, []);

    /// <summary>O modelo prevê melhor que a taxa-base? (Brier skill score positivo.)</summary>
    public bool BeatsBaseRate => Examples > 0 && Brier < BaseRateBrier;

    public static CalibrationReport Calculate(IReadOnlyList<bool> outcomes, IReadOnlyList<double> probabilities, int bins = 5)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 1);
        if (outcomes.Count != probabilities.Count)
        {
            throw new ArgumentException("Quantidade de resultados e probabilidades difere.");
        }

        var n = outcomes.Count;
        if (n == 0)
        {
            return Empty;
        }

        var y = outcomes.Select(o => o ? 1d : 0d).ToArray();
        var p = probabilities.Select(v => Math.Clamp(v, 0, 1)).ToArray();
        var baseRate = y.Average();
        var brier = y.Select((v, i) => (p[i] - v) * (p[i] - v)).Average();
        var baseBrier = y.Select(v => (baseRate - v) * (baseRate - v)).Average();

        var result = new List<CalibrationBin>(bins);
        var ece = 0d;
        for (var b = 0; b < bins; b++)
        {
            var from = (double)b / bins;
            var to = (double)(b + 1) / bins;
            var members = Enumerable.Range(0, n).Where(i => p[i] >= from && (p[i] < to || (b == bins - 1 && p[i] <= to))).ToList();
            if (members.Count == 0)
            {
                result.Add(new CalibrationBin(from, to, 0, 0, 0));
                continue;
            }

            var meanPredicted = members.Average(i => p[i]);
            var observed = members.Average(i => y[i]);
            ece += members.Count / (double)n * Math.Abs(meanPredicted - observed);
            result.Add(new CalibrationBin(from, to, members.Count, Math.Round(meanPredicted, 4), Math.Round(observed, 4)));
        }

        return new CalibrationReport(n, Math.Round(brier, 4), Math.Round(baseBrier, 4), Math.Round(ece, 4), result);
    }
}
