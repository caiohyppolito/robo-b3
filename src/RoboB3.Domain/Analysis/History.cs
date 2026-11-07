namespace RoboB3.Domain.Analysis;

/// <summary>
/// Visão do histórico até a barra atual. Acessar qualquer barra futura lança exceção:
/// é a garantia estrutural de que nenhuma estratégia "enxerga o futuro" no backtest.
/// </summary>
public sealed class History
{
    private readonly IReadOnlyList<Bar> _bars;

    public History(IReadOnlyList<Bar> bars, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentOutOfRangeException.ThrowIfNegative(currentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(currentIndex, bars.Count);
        _bars = bars;
        Count = currentIndex + 1;
    }

    public int Count { get; }

    public Bar Current => _bars[Count - 1];

    /// <summary>Barra a <paramref name="barsAgo"/> barras atrás (0 = atual).</summary>
    public Bar this[int barsAgo]
    {
        get
        {
            if (barsAgo < 0 || barsAgo >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(barsAgo), "Fora do histórico disponível.");
            }

            return _bars[Count - 1 - barsAgo];
        }
    }

    public bool Has(int bars) => Count >= bars;
}
