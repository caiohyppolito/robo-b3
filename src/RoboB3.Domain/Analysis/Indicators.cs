namespace RoboB3.Domain.Analysis;

/// <summary>Indicadores técnicos calculados sobre o histórico disponível (sem olhar o futuro).</summary>
public static class Indicators
{
    /// <summary>Média móvel simples dos fechamentos, terminando <paramref name="offset"/> barras atrás.</summary>
    public static decimal Sma(History h, int period, int offset = 0)
    {
        Require(h, period + offset);
        var sum = 0m;
        for (var i = offset; i < offset + period; i++)
        {
            sum += h[i].Close;
        }

        return sum / period;
    }

    /// <summary>RSI de Cutler (médias simples de ganhos e perdas), de 0 a 100.</summary>
    public static decimal Rsi(History h, int period)
    {
        Require(h, period + 1);
        decimal gains = 0, losses = 0;
        for (var i = 0; i < period; i++)
        {
            var change = h[i].Close - h[i + 1].Close;
            if (change > 0)
            {
                gains += change;
            }
            else
            {
                losses -= change;
            }
        }

        if (losses == 0)
        {
            return gains == 0 ? 50m : 100m;
        }

        var rs = gains / losses;
        return 100m - (100m / (1m + rs));
    }

    /// <summary>Average True Range (média simples do true range).</summary>
    public static decimal Atr(History h, int period)
    {
        Require(h, period + 1);
        var sum = 0m;
        for (var i = 0; i < period; i++)
        {
            var bar = h[i];
            var previousClose = h[i + 1].Close;
            sum += Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
        }

        return sum / period;
    }

    /// <summary>Máxima das <paramref name="period"/> barras anteriores à atual.</summary>
    public static decimal HighestHigh(History h, int period)
    {
        Require(h, period + 1);
        var max = decimal.MinValue;
        for (var i = 1; i <= period; i++)
        {
            max = Math.Max(max, h[i].High);
        }

        return max;
    }

    /// <summary>Mínima das <paramref name="period"/> barras anteriores à atual.</summary>
    public static decimal LowestLow(History h, int period)
    {
        Require(h, period + 1);
        var min = decimal.MaxValue;
        for (var i = 1; i <= period; i++)
        {
            min = Math.Min(min, h[i].Low);
        }

        return min;
    }

    private static void Require(History h, int bars)
    {
        ArgumentNullException.ThrowIfNull(h);
        if (!h.Has(bars))
        {
            throw new InvalidOperationException($"Histórico insuficiente: {h.Count} barras, necessárias {bars}.");
        }
    }
}
