using RoboB3.Domain.Analysis;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

internal static class TestBars
{
    public static readonly DateOnly Start = new(2025, 1, 2);

    /// <summary>Barras a partir dos fechamentos; abertura = fechamento anterior, máxima/mínima ±1%.</summary>
    public static List<Bar> FromCloses(params decimal[] closes)
    {
        var bars = new List<Bar>(closes.Length);
        for (var i = 0; i < closes.Length; i++)
        {
            var open = i == 0 ? closes[0] : closes[i - 1];
            var high = Math.Max(open, closes[i]) * 1.01m;
            var low = Math.Min(open, closes[i]) * 0.99m;
            bars.Add(new Bar(Start.AddDays(i), open, high, low, closes[i], 1000));
        }

        return bars;
    }

    public static List<Bar> Linear(int count, decimal start, decimal step) =>
        FromCloses(Enumerable.Range(0, count).Select(i => start + (i * step)).ToArray());
}

/// <summary>Estratégia roteirizada para testar o motor isoladamente.</summary>
internal sealed class ScriptedStrategy(Func<History, PositionContext, StrategySignal> script, int warmup = 1) : IStrategy
{
    public string Key => "scripted";

    public int WarmupBars => warmup;

    public StrategySignal Evaluate(History history, PositionContext position) => script(history, position);
}
