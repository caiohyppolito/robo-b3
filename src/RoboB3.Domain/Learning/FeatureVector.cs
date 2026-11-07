using RoboB3.Domain.Analysis;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Learning;

/// <summary>Contexto de mercado no momento de um sinal, usado pela IA para estimar a chance de acerto.</summary>
public sealed record FeatureVector
{
    public const int Count = 14;

    public static IReadOnlyList<string> Names { get; } =
    [
        "retorno_1d", "retorno_5d", "retorno_20d", "volatilidade_20d", "atr_pct",
        "rsi_14", "dist_media_20", "dist_media_50", "dist_media_200", "inclinacao_media_50",
        "posicao_no_canal_20", "volume_relativo", "queda_desde_maxima_200", "direcao",
    ];

    public FeatureVector(IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != Count)
        {
            throw new ArgumentException($"Esperadas {Count} variáveis, recebidas {values.Count}.", nameof(values));
        }

        Values = values.ToArray();
    }

    public IReadOnlyList<float> Values { get; }

    public FeatureVector WithValue(int index, float value)
    {
        var copy = Values.ToArray();
        copy[index] = value;
        return new FeatureVector(copy);
    }
}

/// <summary>Extrai o contexto de mercado só com o passado (via <see cref="History"/>).</summary>
public static class FeatureExtractor
{
    /// <summary>Barras necessárias (média de 200 dias + 1).</summary>
    public const int RequiredBars = 201;

    /// <returns>Nulo quando não há histórico suficiente; a IA então não opina.</returns>
    public static FeatureVector? TryExtract(History history, PositionDirection direction)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!history.Has(RequiredBars))
        {
            return null;
        }

        var close = history.Current.Close;
        var sma50 = Indicators.Sma(history, 50);
        var high20 = Indicators.HighestHigh(history, 20);
        var low20 = Indicators.LowestLow(history, 20);
        var range = high20 - low20;

        var values = new[]
        {
            Return(history, 1),
            Return(history, 5),
            Return(history, 20),
            Volatility(history, 20),
            Ratio(Indicators.Atr(history, 14), close),
            (float)(Indicators.Rsi(history, 14) / 100m),
            Ratio(close, Indicators.Sma(history, 20)) - 1f,
            Ratio(close, sma50) - 1f,
            Ratio(close, Indicators.Sma(history, 200)) - 1f,
            Ratio(sma50, Indicators.Sma(history, 50, offset: 10)) - 1f,
            range > 0 ? (float)Math.Clamp((close - low20) / range, -1m, 2m) : 0.5f,
            VolumeRatio(history, 20),
            Ratio(close, Indicators.HighestHigh(history, 200)) - 1f,
            (float)(int)direction,
        };

        return new FeatureVector(values);
    }

    private static float Return(History h, int bars) => Ratio(h[0].Close, h[bars].Close) - 1f;

    private static float Ratio(decimal a, decimal b) => b == 0 ? 0f : (float)(a / b);

    private static float Volatility(History h, int period)
    {
        var returns = Enumerable.Range(0, period).Select(i => (double)((h[i].Close / h[i + 1].Close) - 1m)).ToList();
        var mean = returns.Average();
        return (float)Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (period - 1));
    }

    private static float VolumeRatio(History h, int period)
    {
        var average = Enumerable.Range(1, period).Average(i => (double)h[i].Volume);
        return average > 0 ? (float)Math.Min(10d, h[0].Volume / average) : 1f;
    }
}
