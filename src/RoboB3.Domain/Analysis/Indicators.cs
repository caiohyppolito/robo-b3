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

    /// <summary>
    /// Média móvel exponencial dos fechamentos. Semeada pela média simples e calculada sobre uma janela de
    /// até 10 períodos de histórico, o que deixa o resultado estável e sem depender do início da série.
    /// </summary>
    public static decimal Ema(History h, int period, int offset = 0)
    {
        Require(h, period + offset);
        var available = h.Count - offset;
        var window = Math.Min(available, period * 10);
        var start = offset + window - 1;
        var ema = 0m;
        for (var i = start; i > start - period; i--)
        {
            ema += h[i].Close;
        }

        ema /= period;
        var k = 2m / (period + 1);
        for (var i = start - period; i >= offset; i--)
        {
            ema = (h[i].Close * k) + (ema * (1 - k));
        }

        return ema;
    }

    /// <summary>
    /// VWAP do pregão: média do preço típico (máxima + mínima + fechamento) / 3 ponderada pelo volume,
    /// desde a primeira barra do dia até a atual. Só faz sentido em barras intradiárias.
    /// </summary>
    public static decimal? SessionVwap(History h)
    {
        ArgumentNullException.ThrowIfNull(h);
        var day = h.Current.Date;
        decimal weighted = 0, volume = 0;
        for (var i = 0; i < h.Count && h[i].Date == day; i++)
        {
            var bar = h[i];
            var v = Math.Max(1, bar.Volume);
            weighted += (bar.High + bar.Low + bar.Close) / 3m * v;
            volume += v;
        }

        return volume > 0 ? weighted / volume : null;
    }

    /// <summary>
    /// ADX de Wilder: força da tendência (0 a 100), sem dizer a direção. Acima de 25, tendência definida.
    /// Calculado sobre uma janela de até 5 períodos de histórico.
    /// </summary>
    public static decimal Adx(History h, int period)
    {
        Require(h, (period * 2) + 1);
        var window = Math.Min(h.Count - 1, period * 5);
        decimal trSum = 0, plusSum = 0, minusSum = 0;
        var dxValues = new List<decimal>();
        decimal smoothTr = 0, smoothPlus = 0, smoothMinus = 0;
        var step = 0;
        for (var i = window - 1; i >= 0; i--)
        {
            var bar = h[i];
            var prev = h[i + 1];
            var up = bar.High - prev.High;
            var down = prev.Low - bar.Low;
            var plusDm = up > down && up > 0 ? up : 0;
            var minusDm = down > up && down > 0 ? down : 0;
            var tr = Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - prev.Close), Math.Abs(bar.Low - prev.Close)));
            step++;
            if (step <= period)
            {
                trSum += tr;
                plusSum += plusDm;
                minusSum += minusDm;
                if (step == period)
                {
                    smoothTr = trSum;
                    smoothPlus = plusSum;
                    smoothMinus = minusSum;
                }

                continue;
            }

            smoothTr = smoothTr - (smoothTr / period) + tr;
            smoothPlus = smoothPlus - (smoothPlus / period) + plusDm;
            smoothMinus = smoothMinus - (smoothMinus / period) + minusDm;
            if (smoothTr == 0)
            {
                dxValues.Add(0);
                continue;
            }

            var plusDi = 100m * smoothPlus / smoothTr;
            var minusDi = 100m * smoothMinus / smoothTr;
            var sum = plusDi + minusDi;
            dxValues.Add(sum == 0 ? 0 : 100m * Math.Abs(plusDi - minusDi) / sum);
        }

        if (dxValues.Count == 0)
        {
            return 0;
        }

        var adx = dxValues.Take(period).Average();
        foreach (var dx in dxValues.Skip(period))
        {
            adx = ((adx * (period - 1)) + dx) / period;
        }

        return adx;
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
