using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Analysis;

/// <summary>Barra de preço já ajustada por proventos, usada em análise e backtest.</summary>
public readonly record struct Bar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume)
{
    /// <summary>
    /// Converte candles em barras, aplicando o fator de ajuste (fechamento ajustado / fechamento)
    /// para que dividendos e desdobramentos não apareçam como saltos de preço.
    /// </summary>
    public static IReadOnlyList<Bar> FromCandles(IEnumerable<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        return candles
            .OrderBy(c => c.OpenTime)
            .Select(c =>
            {
                var factor = c.AdjustedClose is > 0 ? c.AdjustedClose.Value / c.Close : 1m;
                return new Bar(
                    DateOnly.FromDateTime(c.OpenTime.UtcDateTime),
                    c.Open * factor,
                    c.High * factor,
                    c.Low * factor,
                    c.Close * factor,
                    c.Volume);
            })
            .ToList();
    }
}
