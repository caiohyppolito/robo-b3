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
public sealed class SmaCrossStrategy(int fast, int slow, decimal atrStop) : IStrategy
{
    private const int AtrPeriod = 14;

    public string Key => StrategyKeys.SmaCross;

    public int WarmupBars => Math.Max(slow + 1, AtrPeriod + 1);

    public StrategySignal Evaluate(History history, PositionContext position)
    {
        var trend = Trend(history, offset: 0);
        var stopDistance = Indicators.Atr(history, AtrPeriod) * atrStop;
        var close = history.Current.Close;

        if (position.Direction == PositionDirection.Flat)
        {
            // Só entra no cruzamento: depois de um stop, espera o próximo sinal em vez de reentrar.
            var crossed = trend != PositionDirection.Flat && Trend(history, offset: 1) != trend;
            return crossed ? new StrategySignal(trend, close - ((int)trend * stopDistance)) : StrategySignal.Flat;
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
public sealed class DonchianBreakoutStrategy(int entryPeriod, int exitPeriod, decimal atrStop) : IStrategy
{
    private const int AtrPeriod = 20;

    public string Key => StrategyKeys.DonchianBreakout;

    public int WarmupBars => Math.Max(Math.Max(entryPeriod, exitPeriod), AtrPeriod) + 1;

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
