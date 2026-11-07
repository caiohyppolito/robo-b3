using RoboB3.Domain.Audit;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Api.Endpoints;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record HaltRequest(string Reason);

public sealed record TradingStateDto(bool IsHalted, string? HaltReason, string? ChangedBy, DateTimeOffset ChangedAt)
{
    public static TradingStateDto From(TradingState state) =>
        new(state.IsHalted, state.HaltReason, state.ChangedBy, state.ChangedAt);
}

public sealed record RiskLimitsDto(
    decimal MaxDailyLossPercent,
    decimal MaxDrawdownPercent,
    decimal MaxPositionPercentPerAsset,
    int MaxOpenPositions,
    int MaxFuturesContracts)
{
    public static RiskLimitsDto From(RiskLimits limits) => new(
        limits.MaxDailyLossPercent,
        limits.MaxDrawdownPercent,
        limits.MaxPositionPercentPerAsset,
        limits.MaxOpenPositions,
        limits.MaxFuturesContracts);

    public RiskLimits ToDomain() => new(
        MaxDailyLossPercent,
        MaxDrawdownPercent,
        MaxPositionPercentPerAsset,
        MaxOpenPositions,
        MaxFuturesContracts);
}

public sealed record PortfolioDto(decimal Equity, decimal DailyLossPercent, decimal DrawdownPercent, int OpenPositions)
{
    public static PortfolioDto From(PortfolioSnapshot snapshot) => new(
        snapshot.Equity,
        Math.Round(snapshot.DailyLossPercent, 2),
        Math.Round(snapshot.DrawdownPercent, 2),
        snapshot.Positions.Count);
}

public sealed record StatusDto(TradingStateDto Trading, RiskLimitsDto Limits, PortfolioDto Portfolio);

public sealed record AssetDto(string Symbol, string Name, AssetClass AssetClass, string? DailyDataSymbol, int LotSize, bool IsActive)
{
    public static AssetDto From(Asset asset) =>
        new(asset.Symbol, asset.Name, asset.AssetClass, asset.DailyDataSymbol, asset.LotSize, asset.IsActive);
}

public sealed record CandleDto(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume)
{
    public static CandleDto From(Candle candle) =>
        new(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
}

public sealed record AuditEntryDto(string Action, string Actor, string? Details, DateTimeOffset OccurredAt)
{
    public static AuditEntryDto From(AuditEntry entry) => new(entry.Action, entry.Actor, entry.Details, entry.OccurredAt);
}
