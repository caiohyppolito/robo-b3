namespace RoboB3.Domain.Learning;

/// <summary>
/// Distribuição de cada variável no treino: limites dos decis e a proporção do treino em cada faixa.
/// Gravada com o modelo para comparar depois com o que o robô está vendo agora.
/// </summary>
public sealed record FeatureBaseline(IReadOnlyList<FeatureBaseline.Feature> Features)
{
    public sealed record Feature(string Name, float[] Cuts, double[] Proportions);

    public static FeatureBaseline FromVectors(IReadOnlyList<FeatureVector> vectors, int bins = 10)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        var features = new List<Feature>(FeatureVector.Count);
        for (var f = 0; f < FeatureVector.Count; f++)
        {
            var values = vectors.Select(v => v.Values[f]).Order().ToArray();
            var cuts = values.Length == 0
                ? []
                : Enumerable.Range(1, bins - 1).Select(q => values[Math.Min(values.Length - 1, q * values.Length / bins)]).Distinct().ToArray();
            features.Add(new Feature(FeatureVector.Names[f], cuts, Proportions(values, cuts)));
        }

        return new FeatureBaseline(features);
    }

    internal static double[] Proportions(IReadOnlyCollection<float> values, float[] cuts)
    {
        var counts = new double[cuts.Length + 1];
        foreach (var v in values)
        {
            var bin = 0;
            while (bin < cuts.Length && v >= cuts[bin])
            {
                bin++;
            }

            counts[bin]++;
        }

        return values.Count == 0 ? counts : counts.Select(c => c / values.Count).ToArray();
    }
}

public enum DriftLevel
{
    InsufficientData = 0,
    Stable = 1,
    Moderate = 2,
    Strong = 3,
}

public sealed record FeatureDriftItem(string Name, double Psi, DriftLevel Level);

/// <summary>
/// Mudança de regime: PSI (índice de estabilidade populacional) de cada variável entre o treino e as decisões recentes.
/// Abaixo de 0,10 estável; até 0,25 moderado; acima, forte. É só um alerta: não troca nem desliga o modelo sozinho
/// (desligar o filtro deixaria entrar mais operações, o que seria afrouxar o risco sem um humano decidir).
/// </summary>
public sealed record DriftReport(int RecentSamples, DriftLevel Level, double MaxPsi, IReadOnlyList<FeatureDriftItem> Features)
{
    public const double ModerateThreshold = 0.10;
    public const double StrongThreshold = 0.25;

    public static DriftReport Compute(FeatureBaseline baseline, IReadOnlyList<FeatureVector> recent, int minSamples = 30)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(recent);
        if (recent.Count < minSamples || baseline.Features.Count != FeatureVector.Count)
        {
            return new DriftReport(recent.Count, DriftLevel.InsufficientData, 0, []);
        }

        var items = new List<FeatureDriftItem>(FeatureVector.Count);
        for (var f = 0; f < FeatureVector.Count; f++)
        {
            var reference = baseline.Features[f];
            var actual = FeatureBaseline.Proportions(recent.Select(v => v.Values[f]).ToList(), reference.Cuts);
            var psi = Psi(reference.Proportions, actual);
            items.Add(new FeatureDriftItem(reference.Name, Math.Round(psi, 4), LevelOf(psi)));
        }

        var max = items.Max(i => i.Psi);
        return new DriftReport(recent.Count, LevelOf(max), max, items.OrderByDescending(i => i.Psi).ToList());
    }

    public static double Psi(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        const double epsilon = 1e-4;
        var psi = 0d;
        for (var i = 0; i < expected.Count; i++)
        {
            var e = Math.Max(expected[i], epsilon);
            var a = Math.Max(actual[i], epsilon);
            psi += (a - e) * Math.Log(a / e);
        }

        return psi;
    }

    private static DriftLevel LevelOf(double psi) =>
        psi > StrongThreshold ? DriftLevel.Strong : psi > ModerateThreshold ? DriftLevel.Moderate : DriftLevel.Stable;
}
