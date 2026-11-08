using RoboB3.Domain.Analysis;

namespace RoboB3.Domain.Strategies;

/// <summary>Referência: compra no início e segura até o fim.</summary>
public sealed class BuyAndHoldStrategy : IStrategy
{
    public string Key => StrategyKeys.BuyAndHold;

    public int WarmupBars => 1;

    public StrategySignal Evaluate(History history, PositionContext position) => new(PositionDirection.Long);
}

/// <summary>
/// Seguidor de tendência: entra no cruzamento da média rápida sobre a lenta (vendido em futuros no cruzamento
/// para baixo) e segura enquanto a tendência durar. Stop móvel a N ATRs do fechamento.
/// </summary>
public sealed class SmaCrossStrategy(int fast, int slow, decimal atrStop, decimal adxMin = 0) : IStrategy
{
    private const int AtrPeriod = 14;
    private const int AdxPeriod = 14;

    public string Key => StrategyKeys.SmaCross;

    public int WarmupBars => Math.Max(slow + 1, adxMin > 0 ? (AdxPeriod * 2) + 1 : AtrPeriod + 1);

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        var trend = Trend(history, offset: 0);
        var stopDistance = Indicators.Atr(history, AtrPeriod) * atrStop;
        var close = history.Current.Close;

        if (position.Direction == PositionDirection.Flat)
        {
            // Só entra no cruzamento: depois de um stop, espera o próximo sinal em vez de reentrar.
            var crossed = trend != PositionDirection.Flat && Trend(history, offset: 1) != trend;
            var trending = adxMin <= 0 || Indicators.Adx(history, AdxPeriod) >= adxMin;
            return crossed && trending ? new StrategySignal(trend, close - ((int)trend * stopDistance)) : StrategySignal.Flat;
        }

        return trend == PositionDirection.Flat
            ? StrategySignal.Flat
            : new StrategySignal(trend, close - ((int)trend * stopDistance));
    }

    private PositionDirection Trend(History history, int offset)
    {
        var fastMa = Indicators.Sma(history, fast, offset);
        var slowMa = Indicators.Sma(history, slow, offset);
        return fastMa > slowMa ? PositionDirection.Long : fastMa < slowMa ? PositionDirection.Short : PositionDirection.Flat;
    }
}

/// <summary>
/// Rompimento de canal (Donchian): entra quando o preço rompe a máxima/mínima de N dias
/// e sai no rompimento contrário de um canal mais curto.
/// </summary>
public sealed class DonchianBreakoutStrategy(int entryPeriod, int exitPeriod, decimal atrStop, decimal adxMin = 0) : IStrategy
{
    private const int AtrPeriod = 20;
    private const int AdxPeriod = 14;

    public string Key => StrategyKeys.DonchianBreakout;

    public int WarmupBars => Math.Max(Math.Max(Math.Max(entryPeriod, exitPeriod), AtrPeriod) + 1, adxMin > 0 ? (AdxPeriod * 2) + 1 : 0);

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        var bar = history.Current;
        var atr = Indicators.Atr(history, AtrPeriod);

        if (position.Direction == PositionDirection.Long && bar.Close < Indicators.LowestLow(history, exitPeriod))
        {
            return StrategySignal.Flat;
        }

        if (position.Direction == PositionDirection.Short && bar.Close > Indicators.HighestHigh(history, exitPeriod))
        {
            return StrategySignal.Flat;
        }

        if (position.Direction != PositionDirection.Flat)
        {
            return new StrategySignal(position.Direction);
        }

        // Filtro de regime: só rompe com tendência definida (ADX), quando ligado.
        if (adxMin > 0 && Indicators.Adx(history, AdxPeriod) < adxMin)
        {
            return StrategySignal.Flat;
        }

        if (bar.Close > Indicators.HighestHigh(history, entryPeriod))
        {
            return new StrategySignal(PositionDirection.Long, bar.Close - (atr * atrStop));
        }

        if (bar.Close < Indicators.LowestLow(history, entryPeriod))
        {
            return new StrategySignal(PositionDirection.Short, bar.Close + (atr * atrStop));
        }

        return StrategySignal.Flat;
    }
}

/// <summary>
/// Reversão à média em tendência de alta: compra quedas fortes (RSI curto muito baixo)
/// com o preço acima da média longa; sai quando o fechamento volta acima da média curta.
/// </summary>
public sealed class RsiMeanReversionStrategy(int rsiPeriod, decimal entryLevel, int trendPeriod, int maxBars) : IStrategy
{
    private const int ExitMaPeriod = 5;
    private const int AtrPeriod = 14;
    private const decimal StopAtr = 3m;

    public string Key => StrategyKeys.RsiMeanReversion;

    public int WarmupBars => Math.Max(trendPeriod, AtrPeriod + 1);

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        var close = history.Current.Close;

        if (position.Direction == PositionDirection.Long)
        {
            var exit = close > Indicators.Sma(history, ExitMaPeriod) || position.BarsHeld >= maxBars;
            return exit ? StrategySignal.Flat : new StrategySignal(PositionDirection.Long);
        }

        var inUptrend = close > Indicators.Sma(history, trendPeriod);
        if (inUptrend && Indicators.Rsi(history, rsiPeriod) < entryLevel)
        {
            return new StrategySignal(PositionDirection.Long, close - (Indicators.Atr(history, AtrPeriod) * StopAtr));
        }

        return StrategySignal.Flat;
    }
}

/// <summary>
/// Intradiário (pensado para WIN em 5 minutos). Hipótese: o cruzamento de uma média curta sobre uma longa,
/// confirmado pelo preço do lado certo da VWAP do dia, marca o início de um movimento que paga um alvo
/// maior que o stop. Regras: compra quando a EMA rápida cruza para cima da SMA lenta na barra fechada e o
/// fechamento está acima da VWAP; venda no espelho. Stop e alvo fixos em pontos; só entra dentro da
/// janela de horário; a posição sai pelo stop, pelo alvo ou no encerramento do day trade.
/// Ideia adaptada do projeto FinSense (MIT); implementação própria.
/// </summary>
public sealed class EmaVwapStrategy(int fast, int slow, decimal stopPoints, decimal targetPoints, int startHhmm, int endHhmm) : IStrategy
{
    public string Key => StrategyKeys.EmaVwap;

    public int WarmupBars => slow + 2;

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        if (position.Direction != PositionDirection.Flat)
        {
            return new StrategySignal(position.Direction);
        }

        var bar = history.Current;
        if (bar.Time is not { } time || Indicators.SessionVwap(history) is not { } vwap)
        {
            return StrategySignal.Flat;
        }

        var local = MarketData.B3Calendar.LocalTime(time);
        var hhmm = (local.Hour * 100) + local.Minute;
        if (hhmm < startHhmm || hhmm > endHhmm)
        {
            return StrategySignal.Flat;
        }

        var fastNow = Indicators.Ema(history, fast);
        var slowNow = Indicators.Sma(history, slow);
        var fastBefore = Indicators.Ema(history, fast, offset: 1);
        var slowBefore = Indicators.Sma(history, slow, offset: 1);
        var close = bar.Close;

        if (fastBefore <= slowBefore && fastNow > slowNow && close > vwap)
        {
            return new StrategySignal(PositionDirection.Long, close - stopPoints, close + targetPoints);
        }

        if (fastBefore >= slowBefore && fastNow < slowNow && close < vwap)
        {
            return new StrategySignal(PositionDirection.Short, close + stopPoints, close - targetPoints);
        }

        return StrategySignal.Flat;
    }
}

/// <summary>
/// Pullback de Dunnigan (ideia registrada a partir do NewRobot; reimplementada, sem código copiado):
/// numa tendência (fechamento acima da média de <paramref name="trendPeriod"/>), uma sequência de pelo menos
/// <paramref name="setupBars"/> barras com máximas e mínimas menores é a correção. Compra com STOP DE ENTRADA
/// um pouco acima da máxima da última barra: só entra se o preço retomar a alta na barra seguinte.
/// Stop abaixo da mínima da correção; alvo em múltiplos do risco. Espelhado para venda.
/// Não aumenta a posição contra o preço (o original permitia; aqui é proibido).
/// </summary>
public sealed class DunniganPullbackStrategy(int setupBars, int trendPeriod, decimal offsetAtr, decimal targetR) : IStrategy
{
    public string Key => StrategyKeys.Dunnigan;

    public int WarmupBars => Math.Max(trendPeriod, setupBars + 15) + 1;

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        if (position.Direction != PositionDirection.Flat)
        {
            // Sai pela corretora (stop/alvo) ou quando a tendência que justificou a entrada acaba.
            var trend = Indicators.Sma(history, trendPeriod);
            var broken = position.Direction == PositionDirection.Long ? history.Current.Close < trend : history.Current.Close > trend;
            return broken ? StrategySignal.Flat : new StrategySignal(position.Direction);
        }

        var sma = Indicators.Sma(history, trendPeriod);
        var offset = Indicators.Atr(history, 14) * offsetAtr;
        var close = history.Current.Close;

        if (close > sma && IsSequence(history, down: true))
        {
            var entry = history.Current.High + offset;
            var stop = SetupExtreme(history, low: true) - offset;
            var risk = entry - stop;
            return risk > 0 ? new StrategySignal(PositionDirection.Long, stop, entry + (risk * targetR), entry) : StrategySignal.Flat;
        }

        if (close < sma && IsSequence(history, down: false))
        {
            var entry = history.Current.Low - offset;
            var stop = SetupExtreme(history, low: false) + offset;
            var risk = stop - entry;
            return risk > 0 ? new StrategySignal(PositionDirection.Short, stop, entry - (risk * targetR), entry) : StrategySignal.Flat;
        }

        return StrategySignal.Flat;
    }

    /// <summary>Mínima (ou máxima) da correção, incluindo a barra atual.</summary>
    private decimal SetupExtreme(History h, bool low)
    {
        var value = low ? h[0].Low : h[0].High;
        for (var i = 1; i < setupBars; i++)
        {
            value = low ? Math.Min(value, h[i].Low) : Math.Max(value, h[i].High);
        }

        return value;
    }

    /// <summary>As últimas barras formam uma sequência de máximas e mínimas menores (ou maiores, para venda).</summary>
    private bool IsSequence(History h, bool down)
    {
        for (var i = 0; i < setupBars; i++)
        {
            var bar = h[i];
            var prev = h[i + 1];
            var ok = down ? bar.High < prev.High && bar.Low < prev.Low : bar.High > prev.High && bar.Low > prev.Low;
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
