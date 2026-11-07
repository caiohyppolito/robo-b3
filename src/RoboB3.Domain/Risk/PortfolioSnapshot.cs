using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Risk;

public sealed record PositionExposure(string Symbol, AssetClass AssetClass, int Quantity, decimal MarketValue);

/// <summary>Fotografia da carteira usada nas checagens de risco.</summary>
public sealed record PortfolioSnapshot
{
    public PortfolioSnapshot(
        decimal equity,
        decimal startOfDayEquity,
        decimal peakEquity,
        IReadOnlyList<PositionExposure> positions)
    {
        Equity = Guard.NotNegative(equity, nameof(equity));
        StartOfDayEquity = Guard.Positive(startOfDayEquity, nameof(startOfDayEquity));
        PeakEquity = Guard.Positive(peakEquity, nameof(peakEquity));
        Positions = positions ?? throw new ArgumentNullException(nameof(positions));
    }

    public decimal Equity { get; }

    public decimal StartOfDayEquity { get; }

    public decimal PeakEquity { get; }

    public IReadOnlyList<PositionExposure> Positions { get; }

    public decimal DailyLossPercent => Math.Max(0m, (StartOfDayEquity - Equity) / StartOfDayEquity * 100m);

    public decimal DrawdownPercent => Math.Max(0m, (PeakEquity - Equity) / PeakEquity * 100m);

    public PositionExposure? FindPosition(string symbol) =>
        Positions.FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
}
