using System.Globalization;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Risk;

namespace RoboB3.Domain.Trading;

/// <summary>Tamanho calculado e a conta, em português, de como se chegou nele.</summary>
public sealed record SizingBreakdown(int Quantity, string Explanation);

/// <summary>
/// Tamanho da posição, igual no backtest e no robô: arrisca no máximo X% do patrimônio até o stop,
/// limitado pela exposição por ativo (ações/ETFs) ou pelo número de contratos (futuros), em lotes inteiros.
/// </summary>
public static class PositionSizer
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static int Size(
        decimal price,
        decimal? stopDistance,
        decimal equity,
        decimal riskPerTradePercent,
        InstrumentSpec instrument,
        RiskLimits limits,
        int contractsInOtherPositions = 0) =>
        Explain(price, stopDistance, equity, riskPerTradePercent, instrument, limits, contractsInOtherPositions).Quantity;

    public static SizingBreakdown Explain(
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
            return new SizingBreakdown(0, "Sem preço ou patrimônio para calcular o tamanho.");
        }

        var unit = instrument.IsFuture ? "contratos" : "ações";
        var parts = new List<string>();
        long quantity = int.MaxValue;
        if (stopDistance is > 0)
        {
            var byRisk = Math.Floor(equity * riskPerTradePercent / 100m / (stopDistance.Value * instrument.Multiplier));
            quantity = (long)Math.Min(int.MaxValue, byRisk);
            parts.Add(string.Format(PtBr, "{0:C0} × {1:0.##}% ÷ ({2:0.##} de stop × {3:0.##}) = {4:N0} {5} pelo risco",
                equity, riskPerTradePercent, stopDistance.Value, instrument.Multiplier, byRisk, unit));
        }
        else
        {
            parts.Add("sem stop: tamanho limitado só pela exposição");
        }

        if (instrument.IsFuture)
        {
            var cap = Math.Max(0, limits.MaxFuturesContracts - contractsInOtherPositions);
            if (cap < quantity)
            {
                quantity = cap;
                parts.Add(string.Format(PtBr, "limitado a {0} pelo teto de {1} contratos futuros", cap, limits.MaxFuturesContracts));
            }
        }
        else
        {
            var maxByExposure = (long)Math.Min(int.MaxValue, Math.Floor(equity * limits.MaxPositionPercentPerAsset / 100m / (price * instrument.Multiplier)));
            if (maxByExposure < quantity)
            {
                quantity = maxByExposure;
                parts.Add(string.Format(PtBr, "limitado a {0:N0} pela exposição máxima de {1:0.##}% por ativo", maxByExposure, limits.MaxPositionPercentPerAsset));
            }
        }

        var lots = (int)(quantity / instrument.LotSize * instrument.LotSize);
        if (lots != quantity)
        {
            parts.Add(string.Format(PtBr, "arredondado para lotes de {0}", instrument.LotSize));
        }

        parts.Add(string.Format(PtBr, "= {0:N0} {1}", lots, unit));
        return new SizingBreakdown(lots, string.Join("; ", parts));
    }
}
