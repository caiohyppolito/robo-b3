using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Risk;

namespace RoboB3.Domain.Trading;

/// <summary>
/// Tamanho da posição, igual no backtest e no robô: arrisca no máximo X% do patrimônio até o stop,
/// limitado pela exposição por ativo (ações/ETFs) ou pelo número de contratos (futuros), em lotes inteiros.
/// </summary>
public static class PositionSizer
{
    public static int Size(
        decimal price,
        decimal? stopDistance,
        decimal equity,
        decimal riskPerTradePercent,
        InstrumentSpec instrument,
        RiskLimits limits,
        int contractsInOtherPositions = 0)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(limits);
        if (price <= 0 || equity <= 0)
        {
            return 0;
        }

        var quantity = stopDistance is > 0
            ? (int)Math.Min(int.MaxValue, Math.Floor(equity * riskPerTradePercent / 100m / (stopDistance.Value * instrument.Multiplier)))
            : int.MaxValue;

        if (instrument.IsFuture)
        {
            quantity = Math.Min(quantity, Math.Max(0, limits.MaxFuturesContracts - contractsInOtherPositions));
        }
        else
        {
            var maxByExposure = (int)Math.Min(int.MaxValue, Math.Floor(equity * limits.MaxPositionPercentPerAsset / 100m / (price * instrument.Multiplier)));
            quantity = Math.Min(quantity, maxByExposure);
        }

        return quantity / instrument.LotSize * instrument.LotSize;
    }
}
