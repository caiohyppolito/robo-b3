namespace RoboB3.Domain.Backtesting;

/// <summary>
/// Taxa livre de risco por dia. Com a série real do CDI, cada pregão rende a taxa daquele dia; em dia sem dado
/// usa a última taxa conhecida; antes do primeiro dado, a taxa anual fixa configurada.
/// </summary>
public sealed class RiskFreeCurve
{
    private readonly SortedList<DateOnly, decimal> _daily;
    private readonly decimal _fallbackDaily;

    public RiskFreeCurve(IEnumerable<KeyValuePair<DateOnly, decimal>> dailyPercent, decimal fallbackAnnualPercent)
    {
        ArgumentNullException.ThrowIfNull(dailyPercent);
        _daily = new SortedList<DateOnly, decimal>(dailyPercent.ToDictionary(p => p.Key, p => p.Value / 100m));
        _fallbackDaily = Constant(fallbackAnnualPercent);
    }

    public int KnownDays => _daily.Count;

    public static decimal Constant(decimal annualPercent) =>
        (decimal)Math.Pow(1d + ((double)annualPercent / 100d), 1d / PerformanceMetrics.TradingDaysPerYear) - 1m;

    /// <summary>Taxa (fração, não %) que o caixa rende no dia útil informado.</summary>
    public decimal DailyRate(DateOnly date)
    {
        if (_daily.TryGetValue(date, out var exact))
        {
            return exact;
        }

        var keys = _daily.Keys;
        int lo = 0, hi = keys.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (keys[mid] <= date)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found >= 0 ? _daily.Values[found] : _fallbackDaily;
    }
}
